using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using NeoSmart.AsyncLock;
using Newtonsoft.Json;
using Octokit;
using Octokit.Internal;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LenovoLegionToolkit.Lib.Resources;

namespace LenovoLegionToolkit.Lib.Utils;

public class UpdateChecker
{
    private readonly HttpClientFactory _httpClientFactory;
    private readonly UpdateSettings _updateSettings = IoCContainer.Resolve<UpdateSettings>();
    private readonly AsyncLock _updateSemaphore = new();

    private static readonly Dictionary<string, ProjectEntry> ProjectEntries = new();
    private const string UrlObfuscatedHex = "0e8bbf50ba83e10b709f8cff655bed580e7b88658f3bfb7c94";
    private static string? _serverUrl;

    private const string TRUSTED_SIGNATURE_THUMBPRINT = "5A6C3448B4D2FECBAA7EE1BB592E4A7EEE6FB7A8";
    private const int MAX_RETRY_COUNT = 3;

    private DateTime _lastUpdate;
    private TimeSpan _minimumTimeSpanForRefresh;
    private Update[] _updates = [];
    public UpdateFromServer? UpdateFromServer;

    public bool Disable { get; set; }
    public UpdateCheckStatus Status { get; set; }

    public UpdateChecker(HttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;

        UpdateMinimumTimeSpanForRefresh();
        _lastUpdate = _updateSettings.Store.LastUpdateCheckDateTime ?? DateTime.MinValue;
    }

    private static string GetServerUrl()
    {
        if (_serverUrl is not null) return _serverUrl;

        var thumbBytes = Encoding.ASCII.GetBytes(TRUSTED_SIGNATURE_THUMBPRINT);
        var keyHash = SHA256.HashData(thumbBytes);
        var obfuscated = Convert.FromHexString(UrlObfuscatedHex);

        for (var i = 0; i < obfuscated.Length; i++)
            obfuscated[i] ^= keyHash[i % keyHash.Length];

        _serverUrl = Encoding.UTF8.GetString(obfuscated);
        return _serverUrl;
    }

    public async Task<Version?> CheckAsync(bool forceCheck)
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
        {
            if (Disable)
            {
                _lastUpdate = DateTime.UtcNow;
                _updates = [];
                return null;
            }

            var timeSpanSinceLastUpdate = DateTime.UtcNow - _lastUpdate;
            var shouldCheck = timeSpanSinceLastUpdate > _minimumTimeSpanForRefresh;

            if (_updateSettings.Store.UpdateMethod == UpdateMethod.GitHub)
            {
                try
                {
                    if (!forceCheck && !shouldCheck)
                        return _updates.Length != 0 ? _updates.First().Version : null;

                    Log.Instance.Trace($"Checking GitHub for updates...");

                    _updates = [];

                    var adapter = new HttpClientAdapter(_httpClientFactory.CreateHandler);
                    var productInformation = new ProductHeaderValue("LenovoLegionToolkit-UpdateChecker");
                    var connection = new Connection(productInformation, adapter);
                    var githubClient = new GitHubClient(connection);
                    var releases = await githubClient.Repository.Release.GetAll("LenovoLegionToolkit-Team", "LenovoLegionToolkit", new ApiOptions { PageSize = 5 }).ConfigureAwait(false);

                    var thisReleaseVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0, 0);
                    var thisBuildDate = Assembly.GetEntryAssembly()?.GetBuildDateTime() ?? new DateTime(2000, 1, 1);
                    var updateChannel = _updateSettings.Store.UpdateChannel;

                    Log.Instance.Trace($"Found {releases.Count} releases. Current: {thisReleaseVersion} built on {thisBuildDate:yyyy-MM-dd}. Channel: {updateChannel}");
                    foreach (var r in releases)
                    {
                        Log.Instance.Trace($"- {r.TagName} (Draft: {r.Draft}, Pre: {r.Prerelease}, Branch: {r.TargetCommitish}, Date: {r.CreatedAt:yyyy-MM-dd})");
                    }

                    var updates = releases
                        .Where(r => !r.Draft)
                        .Where(r => IsMatchingChannel(r, updateChannel))
                        .Where(r => (r.PublishedAt ?? r.CreatedAt).UtcDateTime >= thisBuildDate)
                        .Select(r => new { Release = r, Version = TryParseReleaseVersion(r.TagName) })
                        .Where(r => r.Version is not null && r.Version > thisReleaseVersion)
                        .OrderByDescending(r => r.Version)
                        .Select(r => new Update(r.Release))
                        .ToArray();

                    Log.Instance.Trace($"Checked [updates.Length={updates.Length}]");

                    _updates = updates;
                    Status = UpdateCheckStatus.Success;

                    return _updates.Length != 0 ? _updates.First().Version : null;
                }
                catch (RateLimitExceededException ex)
                {
                    Log.Instance.Trace($"Reached API Rate Limitation.", ex);

                    Status = UpdateCheckStatus.RateLimitReached;
                    return null;
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Error checking for updates via GitHub.", ex);

                    Status = UpdateCheckStatus.Error;
                    return null;
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateSettings.SynchronizeStore();
                }
            }
            else
            {
                try
                {
                    if (!forceCheck && !shouldCheck && UpdateFromServer is not null)
                    {
                        var entry = ProjectEntries.Values
                            .FirstOrDefault(entry => entry.ProjectName == $"LenovoLegionToolkit{GetChannelSuffix(_updateSettings.Store.UpdateChannel)}");
                        var versionString = entry.ProjectVersion ?? "0.0.0.0";
                        return Version.TryParse(versionString, out var parsedVersion) ? parsedVersion : null;
                    }

                    Log.Instance.Trace($"Checking Server for updates...");

                    UpdateFromServer = null;

                    var (currentVersion, newVersion, statusCode, projectInfo, patchNote) = await TryGetUpdateFromServer(_updateSettings.Store.UpdateChannel).ConfigureAwait(false);

                    if (statusCode == StatusCode.Null)
                    {
                        Log.Instance.Trace($"Failed to check for updates.");
                        Status = UpdateCheckStatus.Error;
                        return null;
                    }

                    if (currentVersion == newVersion && statusCode != StatusCode.ForceUpdate)
                    {
                        Log.Instance.Trace($"Already using the latest version.");
                        Status = UpdateCheckStatus.Success;
                        return null;
                    }

                    if (currentVersion > newVersion && statusCode != StatusCode.ForceUpdate)
                    {
                        Log.Instance.Trace($"Using a private version.");
                        Status = UpdateCheckStatus.Success;
                        return null;
                    }

                    if (statusCode is StatusCode.Update or StatusCode.ForceUpdate)
                    {
                        Log.Instance.Trace($"{(statusCode == StatusCode.ForceUpdate ? "Force update" : "Normal update")} available.");
                        Status = UpdateCheckStatus.Success;
                        UpdateFromServer = new UpdateFromServer(projectInfo, patchNote);
                        return newVersion;
                    }

                    Log.Instance.Trace($"No updates available.");
                    Status = UpdateCheckStatus.Success;
                    return null;
                }
                catch (Exception ex)
                {
                    Log.Instance.Trace($"Error checking for updates via Server.", ex);

                    UpdateFromServer = null;
                    Status = UpdateCheckStatus.Error;
                    return null;
                }
                finally
                {
                    _lastUpdate = DateTime.UtcNow;
                    _updateSettings.Store.LastUpdateCheckDateTime = _lastUpdate;
                    _updateSettings.SynchronizeStore();
                }
            }
        }
    }

    public async Task<Update[]> GetUpdatesAsync()
    {
        using (await _updateSemaphore.LockAsync().ConfigureAwait(false))
            return _updates;
    }

    public async Task<string> DownloadLatestUpdateAsync(IProgress<float>? progress = null, CancellationToken cancellationToken = default)
    {
        using (await _updateSemaphore.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var tempPath = Path.Combine(Folders.Temp, $"LenovoLegionToolkitSetup_{Guid.NewGuid()}.exe");

            if (_updateSettings.Store.UpdateMethod == UpdateMethod.GitHub)
            {
                var latestUpdate = _updates.OrderByDescending(u => u.Version).FirstOrDefault();

                if (latestUpdate.Url is null)
                    throw new InvalidOperationException("No GitHub updates available");

                await using var fileStream = File.OpenWrite(tempPath);
                using var httpClient = _httpClientFactory.Create();
                await httpClient.DownloadAsync(latestUpdate.Url, fileStream, progress, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (UpdateFromServer is not { Url: not null })
                    throw new InvalidOperationException("Setup file URL could not be found");

                var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";

                using var httpClient = _httpClientFactory.Create();

                {
                    await using var fileStream = File.OpenWrite(tempPath);
                    await httpClient.DownloadAsync(UpdateFromServer.Value.Url, fileStream, progress, cancellationToken, version).ConfigureAwait(false);
                }

                VerifySignature(tempPath);
            }

            return tempPath;
        }
    }

    public void UpdateMinimumTimeSpanForRefresh() => _minimumTimeSpanForRefresh = _updateSettings.Store.UpdateCheckFrequency switch
    {
        UpdateCheckFrequency.Never => TimeSpan.FromSeconds(0),
        UpdateCheckFrequency.PerHour => TimeSpan.FromHours(1),
        UpdateCheckFrequency.PerThreeHours => TimeSpan.FromHours(3),
        UpdateCheckFrequency.PerTwelveHours => TimeSpan.FromHours(12),
        UpdateCheckFrequency.PerDay => TimeSpan.FromDays(1),
        UpdateCheckFrequency.PerWeek => TimeSpan.FromDays(7),
        UpdateCheckFrequency.PerMonth => TimeSpan.FromDays(30),
        _ => throw new ArgumentException(nameof(_updateSettings.Store.UpdateCheckFrequency))
    };

    private static void VerifySignature(string filePath)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using var baseCert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(baseCert);

            if (!cert.Thumbprint.Equals(TRUSTED_SIGNATURE_THUMBPRINT, StringComparison.OrdinalIgnoreCase))
            {
                var detail = $"Thumbprint mismatch (Actual: {cert.Thumbprint})";
                throw new SecurityException(string.Format(Resource.UpdateChecker_Security_Thumbprint, detail));
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch { /* Ignore */ }

            Log.Instance.Trace($"Signature verification failed for '{filePath}': {ex.Message}");
            throw new SecurityException(string.Format(Resource.UpdateChecker_Security_Invalid, ex.Message), ex);
        }
    }

    #region GitHub Methods

    private static bool IsMatchingChannel(Release release, UpdateChannel updateChannel)
    {
        switch (updateChannel)
        {
            case UpdateChannel.Stable:
                return IsReleaseOnBranch(release, "master") && !release.Prerelease;
            case UpdateChannel.Beta:
                return IsReleaseOnBranch(release, "master") && release.Prerelease;
            case UpdateChannel.Dev:
                return IsReleaseOnBranch(release, "dev") && !release.Prerelease;
            case UpdateChannel.Test:
                return IsReleaseOnBranch(release, "test") && !release.Prerelease;
            default:
                return false;
        }
    }

    private static bool IsReleaseOnBranch(Release release, string branch)
    {
        var target = release.TargetCommitish;

        if (string.IsNullOrWhiteSpace(target))
            return branch.Equals("master", StringComparison.OrdinalIgnoreCase);

        return target.Equals(branch, StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"/{branch}", StringComparison.OrdinalIgnoreCase)
               || target.EndsWith($"\\{branch}", StringComparison.OrdinalIgnoreCase);
    }

    private static Version? TryParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var normalized = tagName.TrimStart('v', 'V');
        if (Version.TryParse(normalized, out var parsed))
            return parsed;

        var numericPart = new string(normalized
            .TakeWhile(c => char.IsDigit(c) || c == '.')
            .ToArray())
            .Trim('.');

        return Version.TryParse(numericPart, out parsed) ? parsed : null;
    }

    #endregion

    #region Server Update Methods

    private static string GetChannelSuffix(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "Beta",
        UpdateChannel.Dev => "Dev",
        UpdateChannel.Test => "Test",
        _ => ""
    };

    private static string GetApiChannelName(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "beta",
        UpdateChannel.Dev => "dev",
        UpdateChannel.Test => "test",
        _ => "stable"
    };

    private async Task<(StatusCode, string)> GetLatestVersionWithRetryAsync(ProjectInfo projectInfo, UpdateChannel channel)
    {
        var (status, version) = await RetryAsync(() => GetLatestVersionFromServer(projectInfo, channel)).ConfigureAwait(false);

        Log.Instance.Trace($"Project: {projectInfo.ProjectName}, Status: {status}, Current: {projectInfo.ProjectCurrentVersion}, Latest: {version}");

        return !string.IsNullOrEmpty(version) ? (status, version) : throw new Exception("Failed to get the latest version.");
    }

    private static async Task<(StatusCode, string)> RetryAsync(Func<Task<(StatusCode, string)>> operation)
    {
        for (var i = 0; i < MAX_RETRY_COUNT; i++)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Attempt {i + 1} failed: {ex.Message}");
                if (i == MAX_RETRY_COUNT - 1) throw;
            }

            await Task.Delay(1000).ConfigureAwait(false);
        }

        return (StatusCode.Null, string.Empty);
    }

    private async Task<(StatusCode, string)> GetLatestVersionFromServer(ProjectInfo projectInfo, UpdateChannel channel)
    {
        using var httpClient = _httpClientFactory.Create();
        var url = $"{GetServerUrl()}/api/v1/projects";

        var userAgent = $"CommonUpdater-LenovoLegionToolkit-{projectInfo.ProjectCurrentVersion ?? "Null"}";
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        var response = await httpClient.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var projectConfig = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
        if (projectConfig is null)
        {
            Log.Instance.Trace($"Project configuration is empty or invalid.");
            return (StatusCode.Null, string.Empty);
        }

        ProjectEntries.Clear();

        if (projectConfig.TryGetValue("maintenanceMode", out var mmObj))
        {
            bool.TryParse(mmObj?.ToString(), out var mm);
            ProjectEntries["MaintenanceMode"] = new ProjectEntry { MaintenanceMode = mm };
        }

        if (!projectConfig.TryGetValue("channels", out var channelsObj))
        {
            Log.Instance.Trace($"Missing 'channels' in response.");
            return (StatusCode.Null, string.Empty);
        }

        var channelsJson = channelsObj?.ToString();
        if (string.IsNullOrEmpty(channelsJson))
        {
            Log.Instance.Trace($"Empty 'channels' in response.");
            return (StatusCode.Null, string.Empty);
        }

        var channels = JsonConvert.DeserializeObject<Dictionary<string, object>>(channelsJson);
        if (channels is null)
        {
            Log.Instance.Trace($"Failed to parse 'channels'.");
            return (StatusCode.Null, string.Empty);
        }

        var channelMapping = new (string ApiKey, string ProjectKey)[]
        {
            ("stable", "LenovoLegionToolkit"),
            ("beta",   "LenovoLegionToolkitBeta"),
            ("dev",    "LenovoLegionToolkitDev"),
            ("test",   "LenovoLegionToolkitTest"),
        };

        foreach (var (apiKey, projectKey) in channelMapping)
        {
            if (!channels.TryGetValue(apiKey, out var chObj))
                continue;

            var chJson = chObj?.ToString();
            if (string.IsNullOrEmpty(chJson))
                continue;

            var details = JsonConvert.DeserializeObject<Dictionary<string, object>>(chJson);
            if (details is null)
                continue;

            if (!details.TryGetValue("version", out var vObj))
                continue;

            var version = vObj?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(version))
                continue;

            var forceUpdate = false;
            if (details.TryGetValue("forceUpdate", out var fObj))
                bool.TryParse(fObj?.ToString(), out forceUpdate);

            var downloadUrl = string.Empty;
            if (details.TryGetValue("downloadUrl", out var dObj))
                downloadUrl = dObj?.ToString() ?? string.Empty;

            ProjectEntries[projectKey] = new ProjectEntry
            {
                ProjectName = projectKey,
                ProjectCurrentVersion = projectInfo.ProjectCurrentVersion ?? string.Empty,
                ProjectVersion = version,
                ProjectForceUpdate = forceUpdate,
                DownloadUrl = downloadUrl
            };
        }

        foreach (var kvp in ProjectEntries)
        {
            if (kvp.Key == "MaintenanceMode")
                Log.Instance.Trace($"MaintenanceMode: {kvp.Value.MaintenanceMode}");
            else
                Log.Instance.Trace($"Project: {kvp.Key}, Version: {kvp.Value.ProjectVersion}, Force Update: {kvp.Value.ProjectForceUpdate}");
        }

        var projectName = $"{projectInfo.ProjectName}{GetChannelSuffix(channel)}";

        if (!ProjectEntries.TryGetValue(projectName, out var entry) || !entry.IsValid())
        {
            Log.Instance.Trace($"Project entry '{projectName}' not found or invalid.");
            return (StatusCode.Null, string.Empty);
        }

        if (!Version.TryParse(entry.ProjectCurrentVersion, out var curVer))
            curVer = new Version(0, 0, 0, 0);

        if (!Version.TryParse(entry.ProjectVersion, out var projVer))
            projVer = new Version(0, 0, 0, 0);

        if (projVer == curVer && !entry.ProjectForceUpdate)
            return (StatusCode.NoUpdate, entry.ProjectVersion);

        return entry.ProjectForceUpdate
            ? (StatusCode.ForceUpdate, entry.ProjectVersion)
            : (StatusCode.Update, entry.ProjectVersion);
    }

    private async Task<(Version?, Version?, StatusCode, ProjectInfo, string)> TryGetUpdateFromServer(UpdateChannel channel)
    {
        var currentVersionString = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0.0";
        var apiChannel = GetApiChannelName(channel);

        var projectInfo = new ProjectInfo
        {
            ProjectName = "LenovoLegionToolkit",
            ProjectExeName = "LenovoLegionToolkitSetup.exe",
            ProjectAuthor = "LenovoLegionToolkit-Team",
            ProjectCurrentVersion = currentVersionString,
            ProjectCurrentExePath = "NULL",
            ProjectNewExePath = $"{GetServerUrl()}/api/v1/download/{apiChannel}/latest"
        };

        var (statusCode, newestVersion) = await GetLatestVersionWithRetryAsync(projectInfo, channel).ConfigureAwait(false);

        if (ProjectEntries.TryGetValue("MaintenanceMode", out var mm) && mm.MaintenanceMode)
        {
            Log.Instance.Trace($"Server is under maintenance mode, channel: {channel}");
            Status = UpdateCheckStatus.Success;
            return (null, null, StatusCode.Null, new ProjectInfo(), string.Empty);
        }

        // Override download URL from API response if available
        var projectKey = $"LenovoLegionToolkit{GetChannelSuffix(channel)}";
        if (ProjectEntries.TryGetValue(projectKey, out var pe) && !string.IsNullOrEmpty(pe.DownloadUrl))
        {
            projectInfo.ProjectNewExePath = new Uri(new Uri(GetServerUrl()), pe.DownloadUrl).ToString();
        }

        projectInfo.ProjectNewVersion = newestVersion;

        if (!Version.TryParse(currentVersionString, out var curVer))
            curVer = new Version(0, 0, 0, 0);

        if (!Version.TryParse(newestVersion, out var newVer))
            newVer = new Version(0, 0, 0, 0);

        if (statusCode is not (StatusCode.Update or StatusCode.ForceUpdate) || string.IsNullOrEmpty(newestVersion))
            return (curVer, newVer, statusCode, projectInfo, string.Empty);

        var patchNote = await FetchPatchNoteAsync(apiChannel, newestVersion, currentVersionString).ConfigureAwait(false);

        return (curVer, newVer, statusCode, projectInfo, patchNote);
    }

    private async Task<string> FetchPatchNoteAsync(string apiChannel, string version, string currentVersionString)
    {
        try
        {
            var langData = "en-US";
            var langPath = Path.Combine(Folders.AppData, "lang");
            if (File.Exists(langPath))
                langData = await File.ReadAllTextAsync(langPath).ConfigureAwait(false);

            var isZh = new CultureInfo(langData).IetfLanguageTag == "zh-Hans";

            var url = $"{GetServerUrl()}/api/v1/projects/{apiChannel}";

            using var httpClient = _httpClientFactory.Create();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"CommonUpdater-LenovoLegionToolkit-{currentVersionString}");

            var response = await httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var info = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
            if (info is null)
                return "No patch notes available.";

            if (!info.TryGetValue("patchNotes", out var notesObj))
                return "No patch notes available.";

            var notesJson = notesObj?.ToString();
            if (string.IsNullOrEmpty(notesJson))
                return "No patch notes available.";

            var notes = JsonConvert.DeserializeObject<Dictionary<string, object>>(notesJson);
            if (notes is null)
                return "No patch notes available.";

            var key = isZh ? "zhHans" : "en";
            if (notes.TryGetValue(key, out var content) && content is string s && !string.IsNullOrWhiteSpace(s))
            {
                Log.Instance.Trace($"Patch note fetched.");
                return s.Trim();
            }

            return "No patch notes available.";
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to fetch patch note: {ex.Message}");
            return "No patch notes available.";
        }
    }
    #endregion
}

public class SecurityException : Exception
{
    public SecurityException() { }
    public SecurityException(string message) : base(message) { }
    public SecurityException(string message, Exception innerException) : base(message, innerException) { }
}