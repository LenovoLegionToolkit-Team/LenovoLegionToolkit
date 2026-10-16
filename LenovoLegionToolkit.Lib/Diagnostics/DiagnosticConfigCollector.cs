using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LenovoLegionToolkit.Lib.Diagnostics;

public sealed class DiagnosticConfigCollector
{
    private static readonly string[] KnownFiles =
    [
        "settings.json", "automation.json", "macro.json", "balancemode.json", "godmode.json", "gpu_oc.json",
        "amd_oc.json", "integrations.json", "itsmode_settings.json", "lamp_array.json", "package_downloader.json",
        "update_settings.json", "notification_settings.json", "special_key.json", "sunrise_sunset.json",
        "spectrum_keyboard.json", "rgb_keyboard.json", "osd.json", "dashboard.json", "sensor_dashboard.json",
        "hardware_sensors.json", "default_amd_overclocking.json", "system_status.json", "args.txt", "lang"
    ];
    private static readonly string[] KnownDirectories = ["Plugins", "Plugin"];

    public void AddToArchive(ZipArchive archive, string appDataPath, DiagnosticRedactor redactor, DiagnosticConfigOptions? options = null)
    {
        options ??= new DiagnosticConfigOptions();

        if (!options.IncludeConfiguration || !Directory.Exists(appDataPath))
        {
            return;
        }
        var included = new List<string>();

        foreach (var fileName in KnownFiles)
        {
            var path = Path.Combine(appDataPath, fileName);

            if (TryAddFile(archive, path, "config/" + fileName, redactor, options.MaxFileBytes))
            {
                included.Add(fileName);
            }
        }

        foreach (var directory in KnownDirectories)
        {
            var root = Path.Combine(appDataPath, directory);

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, "plugin.json", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(appDataPath, path).Replace('\\', '/');

                if (TryAddFile(archive, path, "config/" + relative, redactor, options.MaxFileBytes))
                {
                    included.Add(relative);
                }
            }
        }
        using var index = new StreamWriter(archive.CreateEntry("config/index.json", CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        index.Write(JsonSerializer.Serialize(new
        {
            Root = "config/",
            Files = included.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            Redaction = "Configuration values are sanitized before export; missing files are omitted."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool TryAddFile(ZipArchive archive, string path, string entryName, DiagnosticRedactor redactor, int maxFileBytes)
    {
        if (!File.Exists(path))
        {
            return false;
        }
        var info = new FileInfo(path);

        if (info.Length > maxFileBytes || info.Length < 0)
        {
            return false;
        }
        try
        {
            var content = File.ReadAllText(path);
            var sanitized = redactor.Redact(content) ?? string.Empty;
            using var writer = new StreamWriter(archive.CreateEntry(entryName, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(sanitized);

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
