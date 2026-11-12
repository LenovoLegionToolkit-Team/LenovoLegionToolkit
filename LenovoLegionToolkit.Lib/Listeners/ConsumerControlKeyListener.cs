using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Listeners;

public class ConsumerControlKeyListener(IMainThreadDispatcher mainThreadDispatcher)
    : NativeWindow, IListener<ConsumerControlKeyListener.ChangedEventArgs>
{
    public class ChangedEventArgs(ConsumerControlKey key) : EventArgs
    {
        public ConsumerControlKey Key { get; } = key;
    }

    private const int WM_INPUT = 0x00FF;
    private const uint RIM_TYPEHID = 2;
    private const ushort UsagePageConsumerControl = 0x0C;
    private const ushort UsageConsumerControl = 0x01;
    private const byte ConsumerReportId = 2;
    private const int ConsumerReportLength = 3;
    private const int RawInputBufferLength = 128;
    private const int RawHidDataOffset = 2 * sizeof(uint);

    public bool DiscoveryMode { get; set; }

    public Func<ConsumerControlKey, Task<bool>>? CustomKeyHandler { get; set; }

    public event EventHandler<ChangedEventArgs>? Changed;

    private readonly HashSet<ConsumerControlKey> _seenKeys = [];

    public bool HasSeen(ConsumerControlKey key) => _seenKeys.Contains(key);

    public Task StartAsync() => mainThreadDispatcher.DispatchAsync(() =>
    {
        CreateHandle(new CreateParams
        {
            Caption = "LenovoLegionToolkit_ConsumerControlKeyListenerWindow",
            Parent = new IntPtr(-3)
        });

        RegisterRawInput();

        return Task.CompletedTask;
    });

    public Task StopAsync() => mainThreadDispatcher.DispatchAsync(() =>
    {
        ReleaseHandle();
        return Task.CompletedTask;
    });

    private void RegisterRawInput()
    {
        var devices = new RAWINPUTDEVICE[1];
        devices[0].usUsagePage = UsagePageConsumerControl;
        devices[0].usUsage = UsageConsumerControl;
        devices[0].dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK;
        devices[0].hwndTarget = new HWND(Handle);

        var result = PInvoke.RegisterRawInputDevices(devices, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());

        if (!result)
            Log.Instance.Trace($"Failed to register consumer control raw input. [error={Marshal.GetLastWin32Error()}]");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            HandleInput(new HRAWINPUT(m.LParam));
            return;
        }

        base.WndProc(ref m);
    }

    private void HandleInput(HRAWINPUT hRawInput)
    {
        var headerSize = Marshal.SizeOf<RAWINPUTHEADER>();
        var buffer = new byte[RawInputBufferLength];
        var size = (uint)buffer.Length;

        var read = PInvoke.GetRawInputData(hRawInput, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, buffer, ref size, (uint)headerSize);

        if (read == uint.MaxValue || read == 0 || read > buffer.Length)
            return;

        var header = MemoryMarshal.Read<RAWINPUTHEADER>(buffer);

        if (header.dwType != RIM_TYPEHID)
            return;

        var hid = MemoryMarshal.Read<RAWHID>(buffer.AsSpan(headerSize));

        if (hid.dwSizeHid != ConsumerReportLength || hid.dwCount != 1)
        {
            Log.Instance.Trace($"Consumer control raw input: unexpected HID shape. [sizeHid={hid.dwSizeHid}, count={hid.dwCount}]");
            return;
        }

        var dataOffset = headerSize + RawHidDataOffset;

        if (dataOffset + ConsumerReportLength > read)
        {
            Log.Instance.Trace($"Consumer control raw input: truncated. [read={read}, dataOffset={dataOffset}]");
            return;
        }

        var data = buffer.AsSpan(dataOffset, ConsumerReportLength);

        if (data[0] != ConsumerReportId)
        {
            Log.Instance.Trace($"Consumer control raw input: unexpected report id. [reportId={data[0]}, sizeHid={hid.dwSizeHid}]");
            return;
        }

        var usage = data[1] | (data[2] << 8);

        if (usage == 0)
            return;

        var key = (ConsumerControlKey)usage;

        _seenKeys.Add(key);

        Changed?.Invoke(this, new(key));

        if (!DiscoveryMode)
            _ = HandleAsync(key);
    }

    private async Task HandleAsync(ConsumerControlKey key)
    {
        try
        {
            if (CustomKeyHandler is not null && await CustomKeyHandler(key).ConfigureAwait(false))
                return;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Couldn't handle consumer control key. [key={key}]", ex);
        }
    }
}
