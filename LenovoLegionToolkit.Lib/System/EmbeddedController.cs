using System;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using ZenStates.Core;

namespace LenovoLegionToolkit.Lib.System;

/// <summary>
/// ITE embedded controller RAM access through the I2EC interface (SuperIO ports 0x4E/0x4F),
/// using the signed PawnIO LpcIO module shipped with LibreHardwareMonitor.
/// </summary>
public sealed class EmbeddedController : IDisposable
{
    private const string LPC_IO_MODULE_RESOURCE = "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
    private const string ISA_BUS_MUTEX_NAME = @"Global\Access_ISABUS.HTP.Method";

    private const long SUPER_IO_SLOT_4E = 1;
    private const long INDEX_PORT = 0x2E;
    private const long DATA_PORT = 0x2F;
    private const long I2EC_ADDRESS_LOW = 0x10;
    private const long I2EC_ADDRESS_HIGH = 0x11;
    private const long I2EC_DATA = 0x12;

    private const ushort CHIP_ID_ADDRESS = 0x2000;

    private readonly PawnIo _pawnIo;
    private readonly Mutex _isaBusMutex = new(false, ISA_BUS_MUTEX_NAME);

    public EmbeddedController()
    {
        _pawnIo = PawnIo.LoadModuleFromResource(typeof(Computer).Assembly, LPC_IO_MODULE_RESOURCE);
        _pawnIo.Execute("ioctl_select_slot", [SUPER_IO_SLOT_4E], 0);
    }

    public int ReadChipId()
    {
        var chipId = Read(CHIP_ID_ADDRESS, 2);
        return chipId[0] << 8 | chipId[1];
    }

    public byte[] Read(ushort address, int length)
    {
        var values = new byte[length];
        Locked(() =>
        {
            for (var i = 0; i < length; i++)
            {
                SelectAddress(address + i);
                values[i] = (byte)_pawnIo.Execute("ioctl_superio_inb", [DATA_PORT], 1)[0];
            }
        });
        return values;
    }

    public void Write(ushort address, byte[] values)
    {
        Locked(() =>
        {
            for (var i = 0; i < values.Length; i++)
            {
                SelectAddress(address + i);
                OutB(DATA_PORT, values[i]);
            }
        });
    }

    public void Dispose()
    {
        _pawnIo.Close();
        _isaBusMutex.Dispose();
    }

    private void SelectAddress(int address)
    {
        OutB(INDEX_PORT, I2EC_ADDRESS_HIGH);
        OutB(DATA_PORT, (address >> 8) & 0xFF);
        OutB(INDEX_PORT, I2EC_ADDRESS_LOW);
        OutB(DATA_PORT, address & 0xFF);
        OutB(INDEX_PORT, I2EC_DATA);
    }

    private void OutB(long port, long value) => _pawnIo.Execute("ioctl_superio_outb", [port, value], 0);

    private void Locked(Action action)
    {
        if (!_isaBusMutex.WaitOne(TimeSpan.FromSeconds(1)))
            throw new TimeoutException("ISA bus mutex is busy.");

        try
        {
            action();
        }
        finally
        {
            _isaBusMutex.ReleaseMutex();
        }
    }
}
