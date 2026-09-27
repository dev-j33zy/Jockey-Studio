using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace JockeyStudio.Wpf.Engine;

/// <summary>1:1 of models.rs UpdateInfo: everything the update dialog needs to
/// present the new release and fetch its installer.</summary>
public sealed class UpdateInfo
{
    public string LatestVersion { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public string AssetName { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
}

/// <summary>Self-update check + installer handoff. The check compares the latest
/// GitHub release against the running build; the install downloads the release's
/// installer and hands it to Windows, which elevates it (UAC) and it upgrades the
/// Program Files install in place, then relaunches the app — the caller exits.</summary>
public static class Updater
{
    /// <summary>GitHub repository that hosts this app's releases, e.g. "user/repo".</summary>
    public const string GithubRepo = "dev-j33zy/Jockey-Studio";

    private const string UserAgent = "JockeyStudio-Updater";

    /// <summary>Only the Inno Setup payload is a valid update asset. The app is
    /// installed per-machine under Program Files, so an update is a reinstall —
    /// matching this suffix is what keeps a portable build (or any other .exe on
    /// the release) from being mistaken for the installer.</summary>
    private const string InstallSuffix = "-setup.exe";

    /// <summary>Silent install. The setup's own manifest triggers UAC, so
    /// ShellExecute (not CreateProcess) is what raises the consent prompt;
    /// /CLOSEAPPLICATIONS lets Restart Manager release this app's own locked
    /// executable, and the setup's postinstall run entry starts the new build.</summary>
    private const string InstallArguments =
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>Silently compare the latest GitHub release against the running
    /// build. Returns null when the build is current, when the repo is unset,
    /// or when the release has no installer asset for this OS. Throws when the
    /// check itself fails (network, non-2xx, malformed payload).</summary>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync(string currentVersion)
    {
        string repo = GithubRepo.Trim();
        if (repo.Length == 0 || repo.Equals("owner/repo", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string url = $"https://api.github.com/repos/{repo}/releases/latest";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var gated = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var response = await Client.SendAsync(request, gated.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(gated.Token).ConfigureAwait(false));
        var root = doc.RootElement;

        string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        string published = root.TryGetProperty("published_at", out var p) ? p.GetString() ?? "" : "";

        string? assetName = null;
        string consumerUrl = "";
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var n)) continue;
                string? name = n.GetString();
                if (string.IsNullOrEmpty(name) || !name.EndsWith(InstallSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                assetName = name;
                if (asset.TryGetProperty("browser_download_url", out var u)) consumerUrl = u.GetString() ?? "";
                break;
            }
        }
        if (string.IsNullOrEmpty(assetName))
        {
            throw new InvalidOperationException("no installer asset found");
        }

        string latest = (tag ?? "").TrimStart('v', 'V');
        if (latest.Length == 0 || !IsNewer(latest, currentVersion))
        {
            return null;
        }

        return new UpdateInfo
        {
            LatestVersion = latest,
            CurrentVersion = currentVersion,
            ReleaseNotes = body,
            PublishedAt = published,
            AssetName = assetName,
            DownloadUrl = consumerUrl,
        };
    }

    /// <summary>Download the release's installer and hand it to Windows, which
    /// elevates it; the setup upgrades the install in place and relaunches the
    /// app through its postinstall run entry. The caller is expected to shut the
    /// app down right after this returns.</summary>
    public static async Task InstallUpdateAsync(UpdateInfo info)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jockey-studio-update");
        Directory.CreateDirectory(dir);
        string staged = Path.Combine(dir, "JockeyStudio-setup.exe");

        using (var response = await Client.GetAsync(info.DownloadUrl).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using var fs = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs).ConfigureAwait(false);
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(staged)
        {
            Arguments = InstallArguments,
            // ShellExecute is required for the UAC elevation prompt: a
            // CreateProcess launch of an elevated image fails outright.
            UseShellExecute = true,
        });
    }

    private static bool IsNewer(string latest, string current)
    {
        var (l, c) = (ParseVersion(latest), ParseVersion(current));
        return (l != null && c != null) ? l > c : !string.Equals(latest, current, StringComparison.Ordinal);
    }

    private static Version? ParseVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().TrimStart('v', 'V');
        var m = System.Text.RegularExpressions.Regex.Match(v, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return null;
        int major = int.Parse(m.Groups[1].Value);
        int minor = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        int build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return new Version(major, minor, build);
    }
}