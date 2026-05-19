using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.CLI.Lib.Extensions;

public static class PipeStreamExtensions
{
    private static readonly Encoding Encoding = Encoding.UTF8;

    public static async Task WriteObjectAsync<T>(this PipeStream stream, T obj, CancellationToken token = default)
    {
        if (stream.ReadMode != PipeTransmissionMode.Message)
            throw new InvalidOperationException("ReadMode is not PipeTransmissionMode.Message");

        var str = JsonConvert.SerializeObject(obj);
        var bytes = Encoding.GetBytes(str);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }

    public static async Task<T?> ReadObjectAsync<T>(this PipeStream stream, CancellationToken token = default)
    {
        if (stream.ReadMode != PipeTransmissionMode.Message)
            throw new InvalidOperationException("ReadMode is not PipeTransmissionMode.Message");

        var buffer = new byte[1024];
        using var ms = new global::System.IO.MemoryStream();

        do
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read <= 0) break;
            ms.Write(buffer, 0, read);
        } while (!stream.IsMessageComplete);

        return JsonConvert.DeserializeObject<T>(Encoding.GetString(ms.GetBuffer(), 0, (int)ms.Length));
    }
}
