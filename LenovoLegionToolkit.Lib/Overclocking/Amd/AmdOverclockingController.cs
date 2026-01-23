using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ZenStates.Core;

namespace LenovoLegionToolkit.Lib.Overclocking.Amd;

public sealed class AmdOverclockingController : IDisposable
{
    private const uint PROCHOT_DISABLED_BIT = 0x1000000;
    private const int THRESHOLD = 3;
    private const uint DOWNCORE_CMD_DEFAULT = 0x8000;
    private const uint DOWNCORE_CCD1_DISABLE_ALL = 0x81FF;
    private const uint DOWNCORE_CCD1_ENABLE_ALL = 0x8100;
    private const string WMI_AMD_ACPI = "AMD_ACPI";
    private const string WMI_SCOPE = @"root\wmi";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _internalProfilePath = Path.Combine(Folders.AppData, "amd_overclocking.json");
    private readonly string _statusFilePath = Path.Combine(Folders.AppData, "system_status.json");

    private Cpu? _cpu;
    private MachineInformation? _machineInformation;
    private ManagementObject? _classInstance;
    private bool _isInitialized;

    private List<AmdWmiCommand> _commandList = [];
    private AmdWmiCommand? _cachedDowncoreCmd;

    public bool DoNotApply { get; set; }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized) return;

            _machineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            _cpu = new Cpu();

            UpdateShutdownStatus();
            FetchCommands();

            _isInitialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void UpdateShutdownStatus()
    {
        var info = LoadShutdownInfo();
        var count = info.Status == "Running" ? info.AbnormalCount + 1 : info.AbnormalCount;

        if (count > info.AbnormalCount)
            Log.Instance.Trace($"Abnormal shutdown detected, count: {count}");

        if (count >= THRESHOLD)
        {
            DoNotApply = true;
            Log.Instance.Trace($"Abnormal shutdown limit reached ({THRESHOLD}). Profile application disabled.");
            count = 0;
        }

        SaveShutdownInfo(new ShutdownInfo { Status = "Running", AbnormalCount = count });
    }

    public bool IsSupported() => _isInitialized && _machineInformation?.Properties.IsAmdDevice == true;

    public bool IsActive() => File.Exists(_internalProfilePath);

    public Cpu GetCpu() => _cpu ?? throw new InvalidOperationException(Resource.AmdOverclocking_Not_Initialized_Message);

    public ShutdownInfo LoadShutdownInfo()
    {
        if (!File.Exists(_statusFilePath)) return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        try
        {
            using var stream = File.OpenRead(_statusFilePath);
            return JsonSerializer.Deserialize<ShutdownInfo>(stream);
        }
        catch
        {
            return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        }
    }

    public void SaveShutdownInfo(ShutdownInfo info)
    {
        try
        {
            File.WriteAllText(_statusFilePath, JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save ShutdownInfo failed: {ex.Message}");
        }
    }

    public OverclockingProfile? LoadProfile(string? path = null)
    {
        var targetPath = path ?? _internalProfilePath;
        if (!File.Exists(targetPath)) return null;

        try
        {
            return JsonSerializer.Deserialize<OverclockingProfile>(File.ReadAllText(targetPath));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Load Profile Failed: {ex.Message}");
            return null;
        }
    }

    public void SaveProfile(OverclockingProfile profile, string? path = null)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path ?? _internalProfilePath, JsonSerializer.Serialize(profile, options));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save Profile Failed: {ex.Message}");
        }
    }

    public async Task ApplyProfileAsync(OverclockingProfile profile)
    {
        if (DoNotApply)
        {
            return;
        }

        var status = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);

        switch (status)
        {
            case PowerAdapterStatus.ConnectedLowWattage:
            case PowerAdapterStatus.Disconnected:
                throw new InvalidOperationException(Resource.AmdOverclocking_Ac_Message);
        }

        EnsureInitialized();

        await Task.Run(() =>
        {
            EnableOCMode(profile.ProchotEnabled);

            if (profile.FMax is { } fmax)
            {
                _cpu.SetFMax(fmax);
            }

            if (_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0)
            {
                for (var i = 0; i < profile.CoreValues.Count && i < 16; i++)
                {
                    if (profile.CoreValues[i] is { } val && IsCoreActive(i))
                    {
                        _cpu.SetPsmMarginSingleCore(EncodeCoreMarginBitmask(i), (int)val);
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    public async Task ApplyInternalProfileAsync()
    {
        if (LoadProfile() is { } profile)
        {
            await ApplyProfileAsync(profile).ConfigureAwait(false);
        }
    }

    public bool EnableOCMode(bool prochotEnabled = true)
    {
        EnsureInitialized();
        var args = MakeCmdArgs(prochotEnabled ? 0U : PROCHOT_DISABLED_BIT, _cpu.smu.Rsmu.MAX_ARGS);
        return _cpu.smu.SendSmuCommand(_cpu.smu.Rsmu, _cpu.smu.Rsmu.SMU_MSG_EnableOcMode, ref args) == SMU.Status.OK;
    }

    public uint EncodeCoreMarginBitmask(int coreIndex, int coresPerCCD = 8)
    {
        EnsureInitialized();
        if (_cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
            return (uint)coreIndex;

        var ccdIndex = coreIndex / coresPerCCD;
        var localCoreIndex = coreIndex % coresPerCCD;
        return (uint)(((ccdIndex << 8) | localCoreIndex) << 20);
    }

    public bool IsCoreActive(int coreIndex)
    {
        EnsureInitialized();
        var mapIndex = coreIndex < 8 ? 0 : 1;
        return ((~_cpu.info.topology.coreDisableMap[mapIndex] >> (coreIndex % 8)) & 1) == 1;
    }

    public static uint[] MakeCmdArgs(uint arg = 0, uint maxArgs = 6)
    {
        var cmdArgs = new uint[maxArgs];
        cmdArgs[0] = arg;
        return cmdArgs;
    }

    private string GetWmiInstanceName()
    {
        try
        {
            return WMI.GetInstanceName(WMI_SCOPE, WMI_AMD_ACPI);
        }
        catch
        {
            throw new NotSupportedException(Resource.AmdOverclocking_Not_Supported);
        }
    }

    public void FetchCommands()
    {
        try
        {
            _classInstance?.Dispose();
            _classInstance = new ManagementObject(WMI_SCOPE, $"{WMI_AMD_ACPI}.InstanceName='{GetWmiInstanceName()}'", null);

            var commands = new List<AmdWmiCommand>();
            string[] methods = ["GetObjectID", "GetObjectID2"];

            foreach (var method in methods)
            {
                var pack = WMI.InvokeMethodAndGetValue(_classInstance, method, "pack", null, 0);
                if (pack == null) continue;

                if (pack.GetPropertyValue("ID") is uint[] ids &&
                    pack.GetPropertyValue("IDString") is string[] names &&
                    pack.GetPropertyValue("Length") is byte count)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(names[i])) break;
                        commands.Add(new AmdWmiCommand
                        {
                            Name = names[i],
                            Id = ids[i],
                            IsSet = !names[i].StartsWith("Get", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            _commandList = commands;
            _cachedDowncoreCmd = _commandList.Find(i => i.Name.Contains("Software Downcore Config"));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Fetch WMI commands failed: {ex.Message}");
            _commandList = [];
            _cachedDowncoreCmd = null;
        }
    }

    public async Task ResetAllActiveCoresCoAsync()
    {
        EnsureInitialized();

        if (_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin == 0)
        {
            Log.Instance.Trace($"Current CPU does not support SMU_MSG_SetDldoPsmMargin.");
            return;
        }

        await Task.Run(() =>
        {
            for (var i = 0; i < 16; i++)
            {
                if (IsCoreActive(i))
                {
                    try
                    {
                        uint bitmask = EncodeCoreMarginBitmask(i);
                        _cpu.SetPsmMarginSingleCore(bitmask, 0);
                        Log.Instance.Trace($"Reset CO for Core {i} (Bitmask: 0x{bitmask:X}) to 0.");
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to reset CO for Core {i}: {ex.Message}");
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    public bool SwitchProfile(CpuProfileMode mode)
    {
        EnsureInitialized();

        if (_cachedDowncoreCmd == null)
        {
            Log.Instance.Trace($"Downcore command not supported on this system.");
            return false;
        }

        uint subCommand = mode == CpuProfileMode.X3DGaming ? DOWNCORE_CCD1_DISABLE_ALL : DOWNCORE_CCD1_ENABLE_ALL;

        WMI.RunCommand(_classInstance, _cachedDowncoreCmd.Value.Id, DOWNCORE_CMD_DEFAULT);
        WMI.RunCommand(_classInstance, _cachedDowncoreCmd.Value.Id, subCommand);

        return true;
    }

    [MemberNotNull(nameof(_cpu), nameof(_machineInformation), nameof(_classInstance))]
    private void EnsureInitialized()
    {
        if (!_isInitialized || _cpu == null || _machineInformation == null || _classInstance == null)
        {
            throw new InvalidOperationException(Resource.AmdOverclocking_Not_Initialized_Message);
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _classInstance?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}