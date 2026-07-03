using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;
using System.Runtime.InteropServices;
using LenovoLegionToolkit.Lib.Features.Hybrid;
using LenovoLegionToolkit.Lib.Listeners;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.System;

public sealed class DgpuAwakeManager : IAsyncDisposable, IDisposable
{
    private readonly ApplicationSettings _settings;
    private readonly PowerStateListener _powerStateListener;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private ID3D11Device? _d3dDevice;
    private ID3D11DeviceContext? _d3dContext;
    private bool _isDisposed;
    private bool _isActive;

    public DgpuAwakeManager(ApplicationSettings settings, PowerStateListener powerStateListener)
    {
        _settings = settings;
        _powerStateListener = powerStateListener;

        _powerStateListener.Changed += PowerStateListener_Changed;
        
        _ = UpdateStateAsync();
    }

    private async void PowerStateListener_Changed(object? sender, PowerStateListener.ChangedEventArgs e)
    {
        if (e.PowerStateEvent == LenovoLegionToolkit.Lib.PowerStateEvent.Suspend)
        {
            Log.Instance.Trace($"System suspending: tearing down dGPU awake manager.");
            await StopInternalAsync().ConfigureAwait(false);
        }
        else if (e.PowerStateEvent == LenovoLegionToolkit.Lib.PowerStateEvent.Resume)
        {
            Log.Instance.Trace($"System resumed: restoring dGPU awake manager.");
            await UpdateStateAsync().ConfigureAwait(false);
        }
        else if (e.PowerAdapterStateChanged)
        {
            Log.Instance.Trace($"Power adapter state changed: updating dGPU awake manager.");
            await UpdateStateAsync().ConfigureAwait(false);
        }
    }

    public async Task UpdateStateAsync()
    {
        if (_isDisposed) return;

        var keepAwake = _settings.Store.KeepDgpuAwake;

        if (keepAwake)
        {
            var hybridModeFeature = IoCContainer.Resolve<HybridModeFeature>();
            var state = await hybridModeFeature.GetStateAsync().ConfigureAwait(false);

            if (state is HybridModeState.OnIGPUOnly or HybridModeState.UMA)
            {
                keepAwake = false;
            }
            else if (state == HybridModeState.OnAuto)
            {
                var acStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
                if (acStatus != PowerAdapterStatus.Connected)
                {
                    keepAwake = false;
                }
            }
        }

        if (keepAwake)
        {
            await StartInternalAsync().ConfigureAwait(false);
        }
        else
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
    }

    private async Task StartInternalAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isActive) return;

            Log.Instance.Trace($"Attempting to keep dGPU awake...");
            CreateD3D11Device();
            _isActive = true;
            Log.Instance.Trace($"dGPU awake manager started.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to initialize D3D11 dummy device for dGPU awake: {ex.Message}");
            DisposeD3D11Device();
            _isActive = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task StopInternalAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isActive) return;

            DisposeD3D11Device();
            _isActive = false;
            Log.Instance.Trace($"dGPU awake manager stopped.");
        }
        finally
        {
            _lock.Release();
        }
    }

    private unsafe void CreateD3D11Device()
    {
        PInvoke.CreateDXGIFactory2(0, typeof(IDXGIFactory6).GUID, out object factoryObj);
        var factory = (IDXGIFactory6)factoryObj;

        IDXGIAdapter1? dgpuAdapter = null;
        try
        {
            factory.EnumAdapterByGpuPreference(0, DXGI_GPU_PREFERENCE.DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE, typeof(IDXGIAdapter1).GUID, out object adapterObj);
            dgpuAdapter = (IDXGIAdapter1)adapterObj;
        }
        catch
        {
        }

        if (dgpuAdapter == null)
        {
            Marshal.ReleaseComObject(factory);
            Log.Instance.Trace($"Failed to find any HighPerformance adapter.");
            throw new Exception("No suitable hardware adapter found.");
        }

        try
        {
            PInvoke.D3D11CreateDevice(
                dgpuAdapter, 
                Windows.Win32.Graphics.Direct3D.D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                Windows.Win32.Foundation.HMODULE.Null,
                Windows.Win32.Graphics.Direct3D11.D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                null, 
                0,    
                7u,   
                out _d3dDevice,
                null,
                out _d3dContext);
        }
        finally
        {
            Marshal.ReleaseComObject(dgpuAdapter);
            Marshal.ReleaseComObject(factory);
        }
    }

    private void DisposeD3D11Device()
    {
        if (_d3dContext != null)
        {
            Marshal.ReleaseComObject(_d3dContext);
            _d3dContext = null;
        }

        if (_d3dDevice != null)
        {
            Marshal.ReleaseComObject(_d3dDevice);
            _d3dDevice = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _powerStateListener.Changed -= PowerStateListener_Changed;

        await StopInternalAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _powerStateListener.Changed -= PowerStateListener_Changed;

        StopInternalAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }
}
