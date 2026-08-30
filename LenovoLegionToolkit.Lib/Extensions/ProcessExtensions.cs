using System;
using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class ProcessExtensions
{
    public static string? GetFileName(this Process process, int maxLength = 1024)
    {
        try
        {
            if (process.HasExited)
                return null;
        }
        catch
        {
            return null;
        }

        try
        {
            using var handle = PInvoke.OpenProcess_SafeHandle(
                PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                (uint)process.Id);

            if (handle != null && !handle.IsInvalid)
            {
                Span<char> chars = stackalloc char[maxLength];
                var size = (uint)maxLength;
                if (PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, chars, ref size) && size > 0)
                {
                    return chars[..(int)size].ToString();
                }
            }
        }
        catch { /* Fallback */ }

        try
        {
            Span<char> chars = stackalloc char[maxLength];
            var size = (uint)maxLength;
            if (PInvoke.QueryFullProcessImageName(process.SafeHandle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, chars, ref size) && size > 0)
            {
                return chars[..(int)size].ToString();
            }
        }
        catch { /* Fallback */ }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }
}
