using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Utils;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LenovoLegionToolkit.Lib.System;

public class CantSetUEFIPrivilegeException : Exception;

public class CantMountUEFIPartitionException : Exception;

public class NotEnoughSpaceOnUEFIPartitionException : Exception;

public class InvalidBootLogoImageFormatException : Exception;

public class InvalidBootLogoImageSizeException : Exception;

public static class BootLogo
{
    private const string LBLDESP = "LBLDESP";
    private const string LBLDESP_GUID = "{871455D0-5576-4FB8-9865-AF0824463B9E}";
    private const string LBLDVC = "LBLDVC";
    private const string LBLDVC_GUID = "{871455D1-5576-4FB8-9865-AF0824463C9F}";

    private const uint SCOPE_ATTR = PInvokeExtensions.VARIABLE_ATTRIBUTE_NON_VOLATILE |
                                    PInvokeExtensions.VARIABLE_ATTRIBUTE_BOOTSERVICE_ACCESS |
                                    PInvokeExtensions.VARIABLE_ATTRIBUTE_RUNTIME_ACCESS;

    public static async Task<bool> IsSupportedAsync()
    {
        try
        {
            var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            if (!mi.Properties.SupportsBootLogoChange)
                return false;

            _ = GetInfo();
            _ = GetChecksum();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (bool, Resolution, ImageFormat[], string[]) GetStatus()
    {
        var info = GetInfo();
        return (info.Enabled == 1, new(info.SupportedWidth, info.SupportedHeight), info.SupportedFormat.ImageFormats().ToArray(), info.SupportedFormat.ExtensionFilters().ToArray());
    }

    public static async Task EnableAsync(string sourcePath)
    {
        Log.Instance.Trace($"Enabling logo... [sourcePath={sourcePath}]");

        var info = GetInfo();

        ThrowIfImageInvalid(info, sourcePath);

        var existingVersion = ReadExistingLBLDVCVersion();
        var useSha256 = existingVersion < 0 || existingVersion >= SHA256_VERSION;
        var version = useSha256 ? SHA256_VERSION : existingVersion;

        Log.Instance.Trace($"Existing LBLDVC version: 0x{existingVersion.ToString("X")}, using SHA256: {useSha256}");

        SetInfo(info with { Enabled = 1 });

        char? drive = null;
        try
        {
            drive = await MountEfiPartitionAsync().ConfigureAwait(false);
            if (!drive.HasValue)
            {
                Log.Instance.Trace($"Cannot mount EFI partition.");
                throw new CantMountUEFIPartitionException();
            }

            DeleteLogosOnEfiPartition(drive.Value);

            var destinationPath = CopyLogoToEfiPartition(info, sourcePath, drive.Value);

            byte[] hash;
            if (useSha256)
            {
                hash = SHA256.HashData(File.ReadAllBytes(destinationPath));
            }
            else
            {
                var crc = Crc32Adler.Calculate(destinationPath);
                hash = new byte[32];
                BitConverter.GetBytes(crc).CopyTo(hash, 0);
            }

            WriteChecksum(version, hash);
        }
        finally
        {
            if (drive.HasValue)
                await UnMountEfiPartitionAsync(drive.Value).ConfigureAwait(false);
        }

        await DisableWindowsBootUXAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Enabled logo. [sourcePath={sourcePath}]");
    }

    public static async Task DisableAsync()
    {
        Log.Instance.Trace($"Disabling logo...");

        char? drive = null;
        try
        {
            drive = await MountEfiPartitionAsync().ConfigureAwait(false);
            if (drive.HasValue)
                DeleteLogosOnEfiPartition(drive.Value);
        }
        finally
        {
            if (drive.HasValue)
                await UnMountEfiPartitionAsync(drive.Value).ConfigureAwait(false);
        }

        WriteChecksum(SHA256_VERSION, new byte[32]);
        SetInfo(GetInfo() with { Enabled = 0 });

        await EnableWindowsBootUXAsync().ConfigureAwait(false);

        Log.Instance.Trace($"Disabled logo.");
    }

    private static unsafe BootLogoInfo GetInfo()
    {
        Log.Instance.Trace($"Getting info...");

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BootLogoInfo>());

        try
        {
            if (!TokenManipulator.AddPrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE))
            {
                Log.Instance.Trace($"Cannot set UEFI privileges.");

                throw new CantSetUEFIPrivilegeException();
            }

            var ptrSize = (uint)Marshal.SizeOf<BootLogoInfo>();

            uint size;
            fixed (char* pName = LBLDESP)
            fixed (char* pGuid = LBLDESP_GUID)
                size = PInvoke.GetFirmwareEnvironmentVariableEx(new PCWSTR(pName), new PCWSTR(pGuid), ptr.ToPointer(), ptrSize, null);
            if (size != ptrSize)
                PInvokeExtensions.ThrowIfWin32Error("GetFirmwareEnvironmentVariableEx");

            var str = Marshal.PtrToStructure<BootLogoInfo>(ptr);

            Log.Instance.Trace($"Retrieved info. [enabled={str.Enabled}, supportedWidth={str.SupportedWidth}, supportedHeight={str.SupportedHeight}, supportedFormat={(int)str.SupportedFormat}]");

            return str;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);

            TokenManipulator.RemovePrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE);
        }
    }

    private static unsafe void SetInfo(BootLogoInfo info)
    {
        Log.Instance.Trace($"Setting info... [enabled={info.Enabled}, supportedWidth={info.SupportedWidth}, supportedHeight={info.SupportedHeight}, supportedFormat={(int)info.SupportedFormat}]");

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BootLogoInfo>());

        try
        {
            if (!TokenManipulator.AddPrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE))
            {
                if (Log.Instance.IsTraceEnabled)
                    Log.Instance.Trace($"Cannot set UEFI privileges.");

                throw new CantSetUEFIPrivilegeException();
            }

            Marshal.StructureToPtr(info, ptr, false);
            var ptrSize = (uint)Marshal.SizeOf<BootLogoInfo>();

            bool result;
            fixed (char* pName = LBLDESP)
            fixed (char* pGuid = LBLDESP_GUID)
                result = PInvoke.SetFirmwareEnvironmentVariableEx(new PCWSTR(pName), new PCWSTR(pGuid), ptr.ToPointer(), ptrSize, SCOPE_ATTR);
            if (!result)
                PInvokeExtensions.ThrowIfWin32Error("SetFirmwareEnvironmentVariableEx");

            Log.Instance.Trace($"Info set. [enabled={info.Enabled}, supportedWidth={info.SupportedWidth}, supportedHeight={info.SupportedHeight}, supportedFormat={(int)info.SupportedFormat}]");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);

            TokenManipulator.RemovePrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE);
        }
    }

    private static unsafe BootLogoChecksum GetChecksum()
    {
        Log.Instance.Trace($"Getting checksum...");

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BootLogoChecksum>());

        try
        {
            if (!TokenManipulator.AddPrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE))
            {
                Log.Instance.Trace($"Cannot set UEFI privileges.");
                throw new CantSetUEFIPrivilegeException();
            }

            var ptrSize = (uint)Marshal.SizeOf<BootLogoChecksum>();

            uint size;
            fixed (char* pName = LBLDVC)
            fixed (char* pGuid = LBLDVC_GUID)
                size = PInvoke.GetFirmwareEnvironmentVariableEx(new PCWSTR(pName), new PCWSTR(pGuid), ptr.ToPointer(), ptrSize, null);
            if (size != ptrSize)
                PInvokeExtensions.ThrowIfWin32Error("GetFirmwareEnvironmentVariableEx");

            var result = Marshal.PtrToStructure<BootLogoChecksum>(ptr);
            result.Hash ??= new byte[32];

            Log.Instance.Trace($"Retrieved checksum. [version=0x{result.Version.ToString("X")}]");

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
            TokenManipulator.RemovePrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE);
        }
    }

    private const int SHA256_VERSION = 0x00020003;

    private static int ReadExistingLBLDVCVersion()
    {
        try
        {
            return GetChecksum().Version;
        }
        catch
        {
            return -1;
        }
    }

    private static unsafe void WriteChecksum(int version, byte[] hash)
    {
        Log.Instance.Trace($"Writing checksum... [version=0x{version.ToString("X")}]");

        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<BootLogoChecksum>());

        try
        {
            if (!TokenManipulator.AddPrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE))
            {
                Log.Instance.Trace($"Cannot set UEFI privileges.");
                throw new CantSetUEFIPrivilegeException();
            }

            var ptrSize = (uint)Marshal.SizeOf<BootLogoChecksum>();

            var str = new BootLogoChecksum { Version = version, Hash = hash };
            Marshal.StructureToPtr(str, ptr, false);

            bool result;
            fixed (char* pName = LBLDVC)
            fixed (char* pGuid = LBLDVC_GUID)
                result = PInvoke.SetFirmwareEnvironmentVariableEx(new PCWSTR(pName), new PCWSTR(pGuid), ptr.ToPointer(), ptrSize, SCOPE_ATTR);
            if (!result)
                PInvokeExtensions.ThrowIfWin32Error("SetFirmwareEnvironmentVariableEx");

            Log.Instance.Trace($"Checksum written.");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
            TokenManipulator.RemovePrivileges(TokenManipulator.SE_SYSTEM_ENVIRONMENT_PRIVILEGE);
        }
    }

    private static void DeleteLogosOnEfiPartition(char drive)
    {
        Log.Instance.Trace($"Deleting logos on EFI partition...");

        var directoryPath = $@"{drive}:\EFI\Lenovo\Logo";
        if (!Directory.Exists(directoryPath))
        {
            Log.Instance.Trace($"No logos to delete.");
            return;
        }

        var files = Directory.EnumerateFiles(directoryPath, "mylogo*").ToArray();
        files.ForEach(File.Delete);

        Log.Instance.Trace($"Logos deleted. [count={files.Length}]");
    }

    private static string CopyLogoToEfiPartition(BootLogoInfo info, string sourcePath, char drive)
    {
        Log.Instance.Trace($"Copying logo to EFI partition... [sourcePath={sourcePath}, drive={drive}]");

        if (new DriveInfo($"{drive}:").AvailableFreeSpace < new FileInfo(sourcePath).Length)
        {
            Log.Instance.Trace($"Not enough free space on EFI partition.");
            throw new NotEnoughSpaceOnUEFIPartitionException();
        }

        var sourceExtension = DetectActualExtension(sourcePath);
        var destinationDirectory = Path.Combine($"{drive}:", "EFI", "Lenovo", "Logo");
        var filename = $"mylogo_{info.SupportedWidth}x{info.SupportedHeight}{sourceExtension}";
        var destinationPath = Path.Combine(destinationDirectory, filename);

        Log.Instance.Trace($"Destination path: {destinationPath}");

        Directory.CreateDirectory(destinationDirectory);
        File.Copy(sourcePath, destinationPath, true);

        return destinationPath;
    }

    private static string DetectActualExtension(string sourcePath)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        var magicBytes = br.ReadBytes(4);

        return magicBytes switch
        {
            [0xFF, 0xD8, ..] => ".jpg",
            [0x89, 0x50, 0x4E, 0x47] => ".png",
            [0x42, 0x4D, ..] => ".bmp",
            _ => Path.GetExtension(sourcePath)
        };
    }

    private static void ThrowIfImageInvalid(BootLogoInfo info, string sourcePath)
    {
        Log.Instance.Trace($"Validating image... [sourcePath={sourcePath}, enabled={info.Enabled}, supportedWidth={info.SupportedWidth}, supportedHeight={info.SupportedHeight}, supportedFormat={(int)info.SupportedFormat}]");

        var actualExtension = DetectActualExtension(sourcePath);

        if (!info.SupportedFormat.ExtensionFilters().Contains($"*{actualExtension}"))
        {
            Log.Instance.Trace($"Invalid image format. [actualExtension={actualExtension}]");

            throw new InvalidBootLogoImageFormatException();
        }

        Log.Instance.Trace($"Image valid. [sourcePath={sourcePath}, actualExtension={actualExtension}]");
    }

    private static async Task DisableWindowsBootUXAsync()
    {
        Log.Instance.Trace($"Disabling Windows boot animation...");

        try
        {
            var (result, _) = await CMD.RunAsync("bcdedit", "-set bootuxdisabled on").ConfigureAwait(false);
            Log.Instance.Trace($"Disable Windows boot animation result: {result}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to disable Windows boot animation. [ex={ex.Message}]");
        }
    }

    private static async Task EnableWindowsBootUXAsync()
    {
        Log.Instance.Trace($"Enabling Windows boot animation...");

        try
        {
            var (result, _) = await CMD.RunAsync("bcdedit", "-set bootuxdisabled off").ConfigureAwait(false);
            Log.Instance.Trace($"Enable Windows boot animation result: {result}");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to enable Windows boot animation. [ex={ex.Message}]");
        }
    }

    private static char GetUnusedDriveLetter()
    {
        var letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var usedLetters = DriveInfo.GetDrives().Select(di => di.Name.First()).ToArray();

        Log.Instance.Trace($"Used drive letters: {string.Join(",", usedLetters)}");

        var letter = letters.Last(c => !usedLetters.Contains(c));

        Log.Instance.Trace($"Using '{letter}' letter.");

        return letter;
    }

    private static async Task<char?> MountEfiPartitionAsync()
    {
        var drive = GetUnusedDriveLetter();
        var (result, _) = await CMD.RunAsync("mountvol", $"{drive}: /s").ConfigureAwait(false);
        if (result != 0)
        {
            Log.Instance.Trace($"Failed to mount EFI partition at {drive}:");

            return null;
        }

        Log.Instance.Trace($"EFI partition mounted at {drive}:");

        return drive;
    }

    private static async Task UnMountEfiPartitionAsync(char letter)
    {
        var (result, _) = await CMD.RunAsync("mountvol", $"{letter}: /d").ConfigureAwait(false);

        if (result != 0)
        {
            Log.Instance.Trace($"Failed to un-mount EFI partition from {letter}:");
        }

        Log.Instance.Trace($"EFI partition un-mounted from {letter}:.");
    }
}
