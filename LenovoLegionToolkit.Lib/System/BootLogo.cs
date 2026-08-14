using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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

    public static LogoDiyVersion GetLogoDiyVersion()
    {
        var raw = ReadExistingLBLDVCVersion();
        return new LogoDiyVersion(raw < 0 ? 0U : (uint)raw);
    }

    public static (bool, Resolution, ImageFormat[], string[]) GetStatus()
    {
        var info = GetInfo();
        return (info.Enabled == 1, new(info.SupportedWidth, info.SupportedHeight), info.SupportedFormat.ImageFormats().ToArray(), info.SupportedFormat.ExtensionFilters().ToArray());
    }

    public static async Task<byte[]?> GetActiveCustomLogoBytesAsync()
    {
        var (enabled, _, _, _) = GetStatus();
        if (!enabled)
            return null;

        char? drive = null;
        try
        {
            drive = await MountEfiPartitionAsync().ConfigureAwait(false);
            if (!drive.HasValue)
                return null;

            var directoryPath = $@"{drive.Value}:\EFI\Lenovo\Logo";
            if (!Directory.Exists(directoryPath))
                return null;

            var file = Directory.GetFiles(directoryPath).FirstOrDefault();
            if (file == null || !File.Exists(file))
                return null;

            return await File.ReadAllBytesAsync(file).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to read custom logo from EFI partition.", ex);
            return null;
        }
        finally
        {
            if (drive.HasValue)
                await UnMountEfiPartitionAsync(drive.Value).ConfigureAwait(false);
        }
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
            SetInfo(info with { Enabled = 1 });
        }
        finally
        {
            if (drive.HasValue)
                await UnMountEfiPartitionAsync(drive.Value).ConfigureAwait(false);
        }

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
        var directoryPath = $@"{drive}:\EFI\Lenovo\Logo";
        if (!Directory.Exists(directoryPath))
            return;

        try
        {
            Directory.Delete(directoryPath, true);
        }
        catch (UnauthorizedAccessException)
        {
            var dirInfo = new DirectoryInfo(directoryPath);
            dirInfo.Attributes &= ~FileAttributes.ReadOnly;
            foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
                file.Delete();
            }
            Directory.Delete(directoryPath, true);
        }

        Log.Instance.Trace($"Logo directory deleted. [path={directoryPath}]");
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

        if (sourceExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destinationPath, true);
        }
        else
        {
            ProcessAndSaveStaticImage(sourcePath, destinationPath, sourceExtension, info.SupportedWidth, info.SupportedHeight);
        }

        return destinationPath;
    }

    private static void ProcessAndSaveStaticImage(string sourcePath, string destinationPath, string sourceExtension, int maxWidth, int maxHeight)
    {
        using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var srcImg = Image.FromStream(fs);

        var (targetWidth, targetHeight) = CalculateFitDimensions(srcImg.Width, srcImg.Height, maxWidth, maxHeight);
        var needsResize = targetWidth != srcImg.Width || targetHeight != srcImg.Height;
        var hasAlpha = Image.IsAlphaPixelFormat(srcImg.PixelFormat);
        var isNot24Bpp = srcImg.PixelFormat != PixelFormat.Format24bppRgb;

        if (needsResize || hasAlpha || isNot24Bpp)
        {
            Log.Instance.Trace($"Processing static image... [src={srcImg.Width}x{srcImg.Height}, format={srcImg.PixelFormat}, target={targetWidth}x{targetHeight}, hasAlpha={hasAlpha}, needsResize={needsResize}]");

            using var destBmp = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(destBmp))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(srcImg, new Rectangle(0, 0, targetWidth, targetHeight));
            }

            var outputFormat = sourceExtension.ToLowerInvariant() switch
            {
                ".bmp" => ImageFormat.Bmp,
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                _ => ImageFormat.Png
            };

            destBmp.Save(destinationPath, outputFormat);
            Log.Instance.Trace($"Processed and saved static image to EFI partition. [path={destinationPath}, format={outputFormat}]");
        }
        else
        {
            File.Copy(sourcePath, destinationPath, true);
            Log.Instance.Trace($"Copied untouched static image to EFI partition. [path={destinationPath}]");
        }
    }

    private static (int Width, int Height) CalculateFitDimensions(int originalWidth, int originalHeight, int maxWidth, int maxHeight)
    {
        if (originalWidth <= maxWidth && originalHeight <= maxHeight)
            return (originalWidth, originalHeight);

        var ratioX = (double)maxWidth / originalWidth;
        var ratioY = (double)maxHeight / originalHeight;
        var ratio = Math.Min(ratioX, ratioY);

        var newWidth = Math.Max(1, (int)Math.Round(originalWidth * ratio));
        var newHeight = Math.Max(1, (int)Math.Round(originalHeight * ratio));

        return (newWidth, newHeight);
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
            [0x47, 0x49, 0x46, 0x38, ..] => ".gif",
            _ => Path.GetExtension(sourcePath)
        };
    }

    private static void ThrowIfImageInvalid(BootLogoInfo info, string sourcePath)
    {
        Log.Instance.Trace($"Validating image... [sourcePath={sourcePath}, enabled={info.Enabled}, supportedWidth={info.SupportedWidth}, supportedHeight={info.SupportedHeight}, supportedFormat={(int)info.SupportedFormat}]");

        var actualExtension = DetectActualExtension(sourcePath);

        if (!info.SupportedFormat.ExtensionFilters().Contains($"*{actualExtension}", StringComparer.OrdinalIgnoreCase))
        {
            Log.Instance.Trace($"Invalid image format. [actualExtension={actualExtension}]");

            throw new InvalidBootLogoImageFormatException();
        }

        if (actualExtension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var img = Image.FromStream(fs);
            if (img.Width > info.SupportedWidth || img.Height > info.SupportedHeight)
            {
                Log.Instance.Trace($"GIF dimensions ({img.Width}x{img.Height}) exceed maximum supported ({info.SupportedWidth}x{info.SupportedHeight}).");
                throw new InvalidBootLogoImageSizeException();
            }
        }

        Log.Instance.Trace($"Image valid. [sourcePath={sourcePath}, actualExtension={actualExtension}]");
    }

    public static async Task<bool> IsWindowsBootAnimationDisabledAsync()
    {
        try
        {
            var (result, output) = await CMD.RunAsync("bcdedit", "/enum {current}").ConfigureAwait(false);
            if (result == 0 && output != null)
            {
                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                var line = lines.FirstOrDefault(l => l.TrimStart().StartsWith("bootuxdisabled", StringComparison.OrdinalIgnoreCase));
                if (line != null)
                {
                    return line.Contains("Yes", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("on", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("true", StringComparison.OrdinalIgnoreCase) ||
                           line.Contains("1");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to query boot animation status.", ex);
        }

        return false;
    }

    public static async Task SetWindowsBootAnimationAsync(bool disable)
    {
        var arg = disable ? "on" : "off";
        Log.Instance.Trace($"Setting bootuxdisabled {arg}...");

        var (result, _) = await CMD.RunAsync("bcdedit", $"-set bootuxdisabled {arg}").ConfigureAwait(false);
        Log.Instance.Trace($"bootuxdisabled {arg} result: {result}");
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
