using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenaissanceDedicated;

/// <summary>One release on GitHub: what it calls itself and the zip to fetch.</summary>
sealed class UpdateRelease
{
    public string Tag;            // v0.1.31
    public string Version;        // 0.1.31
    public string Name;           // Reach Renaissance 0.1.31
    public string AssetName;      // ReachRenaissance-v0.1.31.zip
    public string DownloadUrl;
    public long Size;             // bytes
    public string Sha256;         // lowercase hex, from the release when it says
    public string PageUrl;
}

/// <summary>What the release page says, next to what is installed here.</summary>
sealed class UpdateCheck
{
    public bool Ok;
    public string Error;
    public string Installed = "";  // 0.1.26
    public string InstalledLine = ""; // the whole VERSION.txt line
    public UpdateRelease Release;
    public bool Newer;
    public UpdateStage Stage;      // set by CheckAndStage once a newer build is unpacked and verified
}

/// <summary>A verified, unpacked release waiting to be moved into the game folder.</summary>
sealed class UpdateStage
{
    public bool Ready;
    public string Error;
    public UpdateRelease Release;
    public string Root;        // the folder inside the zip that holds bin/, data/, VERSION.txt
    public string Directory;   // updates/staging, removed once it is installed
    public string Zip;         // updates/download/<asset>, removed once it is unpacked
}

/// <summary>
/// Keeps a dedicated host on the game's newest release. It reads the release page
/// for the configured repository, compares its version with VERSION.txt, and when
/// there is a newer one downloads the zip, checks its SHA-256, unpacks it beside
/// the game and swaps it in. The host stops its servers around that swap and starts
/// them again on the new build; this class never starts or stops a server itself.
/// </summary>
static class Updater
{
    const string Api = "https://api.github.com";
    static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(60);

    public static string UpdatesDir(DedicatedConfig cfg) => Path.Combine(cfg.Root, "updates");

    /// <summary>What the release page calls the newest stable release, and whether it is newer.</summary>
    public static UpdateCheck Check(DedicatedConfig cfg)
    {
        var result = new UpdateCheck
        {
            InstalledLine = GameBuild.Version(cfg),
        };
        result.Installed = VersionOf(result.InstalledLine);

        try
        {
            using var http = Client(ApiTimeout);
            string url = $"{Api}/repos/{cfg.Update.Repo}/releases/latest";
            using var response = http.GetAsync(url).GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                result.Error = response.StatusCode == HttpStatusCode.NotFound
                    ? $"there is no repository {cfg.Update.Repo}, or it has no published releases"
                    : GitHubError(response, body);
                return result;
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                result.Error = "the newest release is still a draft";
                return result;
            }
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
            {
                result.Error = "the newest release is a prerelease";
                return result;
            }

            string tag = Text(root, "tag_name");
            var release = new UpdateRelease
            {
                Tag = tag,
                Version = VersionOf(tag.Length > 0 ? tag : Text(root, "name")),
                Name = Text(root, "name"),
                PageUrl = Text(root, "html_url"),
            };
            if (release.Version.Length == 0)
            {
                result.Error = $"the newest release's tag \"{tag}\" has no version number in it";
                return result;
            }

            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = Text(asset, "name");
                    if (name.Length == 0 || !Matches(cfg.Update.Asset, name)) continue;
                    release.AssetName = name;
                    release.DownloadUrl = Text(asset, "browser_download_url");
                    release.Sha256 = Digest(asset);
                    if (asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number)
                        release.Size = size.GetInt64();
                    break;
                }
            }
            if (release.AssetName == null)
            {
                result.Error = $"release {release.Tag} has no asset named like {cfg.Update.Asset}";
                return result;
            }
            if (!IsGitHubUrl(release.DownloadUrl))
            {
                result.Error = $"release {release.Tag}'s download address isn't on github.com";
                return result;
            }

            result.Release = release;
            result.Newer = Compare(release.Version, result.Installed) > 0;
            result.Ok = true;
            return result;
        }
        catch (Exception e)
        {
            result.Error = Friendly(e);
            return result;
        }
    }

    /// <summary>
    /// Checks, downloads and unpacks a newer release, leaving it ready for Install.
    /// Nothing under the game folder is touched here, so a failed download costs
    /// the running servers nothing. The caller does the stopping and starting.
    /// </summary>
    public static UpdateStage Stage(DedicatedConfig cfg, Action<string> log)
    {
        var stage = new UpdateStage();
        var check = Check(cfg);
        if (!check.Ok)
        {
            stage.Error = check.Error;
            return stage;
        }
        if (!check.Newer)
        {
            stage.Error = $"the installed game ({check.Installed}) is already the newest release";
            return stage;
        }

        var release = check.Release;
        string updates = UpdatesDir(cfg);
        string downloadDir = Path.Combine(updates, "download");
        string stagingDir = Path.Combine(updates, "staging");
        stage.Release = release;
        stage.Directory = stagingDir;

        try
        {
            Clean(downloadDir);
            Clean(stagingDir);
            Directory.CreateDirectory(downloadDir);
            Directory.CreateDirectory(stagingDir);

            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(updates)) ?? cfg.Root);
            long needed = release.Size > 0 ? release.Size * 3 : 1_000_000_000;
            if (drive.IsReady && drive.AvailableFreeSpace < needed)
            {
                stage.Error = $"there isn't room to unpack the update: it needs about " +
                              $"{Megabytes(needed)} free next to the game, and this disk has " +
                              $"{Megabytes(drive.AvailableFreeSpace)}";
                return stage;
            }

            stage.Zip = Path.Combine(downloadDir, release.AssetName);
            log($"update: downloading {release.AssetName} ({Megabytes(release.Size)})");
            Download(release, stage.Zip, log);

            string actual = Hash(stage.Zip);
            if (release.Sha256.Length == 0)
            {
                // Older releases only list their sums in the SHA256SUMS.txt asset.
                release.Sha256 = SumFromAsset(cfg, release);
            }
            if (release.Sha256.Length > 0 && !actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(stage.Zip);
                stage.Error = $"the downloaded file's SHA-256 ({actual}) isn't the one the release lists " +
                              $"({release.Sha256}); nothing was installed";
                return stage;
            }
            log(release.Sha256.Length > 0
                ? "update: the download matches the release's SHA-256"
                : "update: the download is complete (the release lists no SHA-256)");

            log("update: unpacking");
            Extract(stage.Zip, stagingDir);
            TryDelete(stage.Zip);

            string root = InstallRoot(stagingDir);
            if (root == null)
            {
                stage.Error = "the release zip has no bin/host-probe.exe in it; nothing was installed";
                return stage;
            }
            stage.Root = root;

            string stagedVersion = VersionOf(GameBuild.At(root));
            if (stagedVersion.Length == 0)
            {
                stage.Error = "the unpacked release has no VERSION.txt; nothing was installed";
                return stage;
            }
            if (Compare(stagedVersion, release.Version) != 0)
            {
                stage.Error = $"the release says {release.Version} but its own VERSION.txt says " +
                              $"{stagedVersion}; nothing was installed";
                return stage;
            }

            stage.Ready = true;
            return stage;
        }
        catch (Exception e)
        {
            stage.Error = Friendly(e);
            return stage;
        }
    }

    /// <summary>
    /// Moves a staged release over the game folder. mcc/ and anything this host
    /// doesn't own are left where they are. The old files are held in updates/backup
    /// until every new one is in place; if a move fails, the old build goes back.
    /// Only call this while no server is running.
    /// </summary>
    public static bool Install(DedicatedConfig cfg, UpdateStage stage, Action<string> log, out string error)
    {
        error = null;
        if (!stage.Ready || string.IsNullOrEmpty(stage.Root) || !Directory.Exists(stage.Root))
        {
            error = stage.Error ?? "no unpacked release is ready to install";
            return false;
        }

        string game = cfg.Game;
        if (string.IsNullOrEmpty(game) || !Directory.Exists(game))
        {
            error = $"the game folder {game} isn't there";
            return false;
        }

        string backup = Path.Combine(UpdatesDir(cfg), "backup");
        var entries = new List<string>();
        foreach (string folder in new[] { "bin", "data", "themes", "licenses" })
        {
            // bin/ is the engine and always comes; data/ and themes/ are the
            // client's menus and armor, and are only replaced in a folder that
            // already has them. A dedicated-only install stays at ~23 MB instead
            // of growing by the client's 530 MB on its first update.
            if (!Directory.Exists(Path.Combine(stage.Root, folder))) continue;
            if (folder == "bin" || Directory.Exists(Path.Combine(game, folder)))
                entries.Add(folder);
        }
        if (File.Exists(Path.Combine(stage.Root, "VERSION.txt")))
            entries.Add("VERSION.txt");
        if (entries.Count == 0)
        {
            error = "the unpacked release has nothing to install";
            return false;
        }

        bool swapping = false, restored = true;
        try
        {
            Directory.CreateDirectory(UpdatesDir(cfg));
            Clean(backup);
            Directory.CreateDirectory(backup);

            var movedOut = new List<string>();
            try
            {
                swapping = true;
                foreach (string entry in entries)
                {
                    string target = Path.Combine(game, entry);
                    if (Directory.Exists(target)) Directory.Move(target, Path.Combine(backup, entry));
                    else if (File.Exists(target)) File.Move(target, Path.Combine(backup, entry));
                    movedOut.Add(entry);

                    string fresh = Path.Combine(stage.Root, entry);
                    if (Directory.Exists(fresh)) Directory.Move(fresh, target);
                    else if (File.Exists(fresh)) File.Move(fresh, target);
                }
            }
            catch
            {
                // Put the build back exactly as it was. A half-swapped game is worse
                // than an old one.
                foreach (string entry in movedOut)
                {
                    string target = Path.Combine(game, entry);
                    string old = Path.Combine(backup, entry);
                    try
                    {
                        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                        else if (File.Exists(target)) File.Delete(target);
                        if (Directory.Exists(old)) Directory.Move(old, target);
                        else if (File.Exists(old)) File.Move(old, target);
                    }
                    catch
                    {
                        // Best effort: the error that caused this is what matters.
                        restored = false;
                    }
                }
                throw;
            }

            string installed = VersionOf(GameBuild.At(game));
            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch
            {
                // A leftover copy of the old build is harmless.
            }
            Clean(stage.Directory);
            WriteState(cfg, stage.Release, installed);

            log($"update: installed {installed}" +
                (stage.Release.Version.Length > 0 && stage.Release.Version != installed
                    ? $" (release {stage.Release.Version})" : ""));
            return true;
        }
        catch (Exception e)
        {
            error = Friendly(e) + (swapping
                ? restored
                    ? ". The previous game files were put back."
                    : $". The previous build is in {Path.Combine(UpdatesDir(cfg), "backup")}"
                : "");
            return false;
        }
    }

    /// <summary>One check, with every line reported through log. Used by the host.</summary>
    public static UpdateCheck CheckAndStage(DedicatedConfig cfg, Action<string> log)
    {
        var check = Check(cfg);
        if (!check.Ok || !check.Newer) return check;

        log($"update: {check.Installed} is installed; {check.Release.Version} is out");
        var stage = Stage(cfg, log);
        if (!stage.Ready)
        {
            // Newer stays true: the release is out, this attempt just didn't get
            // it. The host reports the error, not "up to date".
            check.Error = stage.Error;
            return check;
        }
        check.Stage = stage;
        return check;
    }

    // ---- release page ------------------------------------------------------

    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("project-renaissance-dedicated/" +
            (typeof(Updater).Assembly.GetName().Version?.ToString() ?? "1.0"));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";

    /// <summary>GitHub computes each asset's digest now; older uploads only have SHA256SUMS.txt.</summary>
    static string Digest(JsonElement asset)
    {
        string digest = Text(asset, "digest");
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return digest.Substring(7).Trim().ToLowerInvariant();
        return "";
    }

    static string GitHubError(HttpResponseMessage response, string body)
    {
        if (response.StatusCode == HttpStatusCode.Forbidden &&
            response.Headers.TryGetValues("x-ratelimit-remaining", out var left) &&
            left.FirstOrDefault() == "0")
            return "GitHub is rate-limiting update checks from this address; it tries again at the next check";
        string message = "";
        try
        {
            using var json = JsonDocument.Parse(body);
            message = Text(json.RootElement, "message");
        }
        catch
        {
            // Not JSON: the status code is all there is.
        }
        return $"GitHub answered {(int)response.StatusCode}" +
               (message.Length > 0 ? $": {message}" : "");
    }

    static bool IsGitHubUrl(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    // ---- download and unpack ----------------------------------------------

    static void Download(UpdateRelease release, string path, Action<string> log)
    {
        using var http = Client(DownloadTimeout);
        using var response = http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                                 .GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new IOException($"the download failed: {GitHubError(response, body)}");
        }
        if (!IsGitHubUrl(response.RequestMessage?.RequestUri?.ToString() ?? ""))
            throw new IOException("the download was redirected away from github.com");
        if (release.Size > 0 && response.Content.Headers.ContentLength is long length && length != release.Size)
            throw new IOException($"the release lists {release.Size} bytes but the download is {length}");

        using var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                          128 * 1024, FileOptions.SequentialScan);
        var buffer = new byte[128 * 1024];
        long total = 0, reported = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            target.Write(buffer, 0, read);
            total += read;
            int percent = release.Size > 0 ? (int)(total * 100 / release.Size) : 0;
            if (release.Size > 0 && percent >= reported + 25)
            {
                reported = percent / 25 * 25;
                log($"update: downloaded {reported}%");
            }
        }
        if (release.Size > 0 && total != release.Size)
            throw new IOException($"the download stopped at {total} of {release.Size} bytes");
    }

    /// <summary>
    /// Unpacks a release, treating a backslash in a zip entry as a folder
    /// separator. The game's own zips are written that way on Windows, and on
    /// Linux those names would otherwise become one long file name. Entries that
    /// try to leave the staging folder are refused.
    /// </summary>
    static void Extract(string zip, string destination)
    {
        string root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            bool folder = entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                          entry.FullName.EndsWith("\\", StringComparison.Ordinal);
            string name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (name.Length == 0) continue;
            if (name.Split('/').Any(part => part == ".."))
                throw new IOException($"the release contains an unsafe path: {entry.FullName}");
            string target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException($"the release contains an unsafe path: {entry.FullName}");
            if (folder)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>SHA256SUMS.txt from the same release, when the asset has no digest of its own.</summary>
    static string SumFromAsset(DedicatedConfig cfg, UpdateRelease release)
    {
        try
        {
            using var http = Client(ApiTimeout);
            string api = $"{Api}/repos/{cfg.Update.Repo}/releases/latest";
            using var response = http.GetAsync(api).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return "";
            using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (!json.RootElement.TryGetProperty("assets", out var assets)) return "";
            foreach (var asset in assets.EnumerateArray())
            {
                if (!Text(asset, "name").Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) continue;
                string sums = http.GetStringAsync(Text(asset, "browser_download_url")).GetAwaiter().GetResult();
                foreach (string line in sums.Split('\n'))
                {
                    string clean = line.Trim();
                    if (!clean.EndsWith(release.AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    string hash = clean.Split(' ', '\t')[0].Trim();
                    if (hash.Length == 64 && hash.All(Uri.IsHexDigit)) return hash.ToLowerInvariant();
                }
            }
        }
        catch
        {
            // No sums to check the download against: it is still checked by size.
        }
        return "";
    }

    /// <summary>The folder in an unpacked release that holds bin/host-probe.exe.</summary>
    static string InstallRoot(string folder)
    {
        if (HoldsEngine(folder)) return folder;
        try
        {
            foreach (string child in Directory.GetDirectories(folder))
                if (HoldsEngine(child)) return child;
            foreach (string child in Directory.GetDirectories(folder))
                foreach (string inner in Directory.GetDirectories(child))
                    if (HoldsEngine(inner)) return inner;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    static bool HoldsEngine(string folder) =>
        File.Exists(Path.Combine(folder, "bin", "host-probe.exe")) ||
        File.Exists(Path.Combine(folder, "bin", "host-probe"));

    static void WriteState(DedicatedConfig cfg, UpdateRelease release, string installed)
    {
        try
        {
            var state = new Dictionary<string, object>
            {
                ["version"] = installed,
                ["tag"] = release.Tag,
                ["release"] = release.Name,
                ["repo"] = cfg.Update.Repo,
                ["page"] = release.PageUrl,
                ["installed"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            Directory.CreateDirectory(UpdatesDir(cfg));
            File.WriteAllText(Path.Combine(UpdatesDir(cfg), "state.json"),
                              JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // The new build is already in place; a missing receipt is not worth failing over.
        }
    }

    // ---- versions ----------------------------------------------------------

    static readonly Regex VersionPattern = new(@"(?<!\d)(\d+\.\d+(?:\.\d+)?)(?!\d)", RegexOptions.Compiled);

    public static string VersionOf(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var match = VersionPattern.Match(line);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>Compares 0.1.31 with 0.1.26 numerically, part by part.</summary>
    public static int Compare(string a, string b)
    {
        string[] left = (a ?? "").Split('.');
        string[] right = (b ?? "").Split('.');
        int count = Math.Max(left.Length, right.Length);
        for (int i = 0; i < count; i++)
        {
            long x = i < left.Length && long.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out long l) ? l : 0;
            long y = i < right.Length && long.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out long r) ? r : 0;
            if (x != y) return x < y ? -1 : 1;
        }
        return 0;
    }

    /// <summary>ReachRenaissance-*.zip against ReachRenaissance-v0.1.31.zip.</summary>
    static bool Matches(string pattern, string name)
    {
        int p = 0, n = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == name[n] ||
                (pattern[p] == '?' && name[n] != '/')))
            {
                p++;
                n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++mark;
            }
            else
            {
                return false;
            }
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    // ---- small helpers -----------------------------------------------------

    static void Clean(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static string Megabytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
        : $"{Math.Max(1, bytes / (1024 * 1024))} MB";

    static string Friendly(Exception e)
    {
        for (Exception inner = e; inner != null; inner = inner.InnerException)
        {
            if (inner is HttpRequestException)
                return "couldn't reach GitHub; check this machine's internet connection";
            if (inner is TaskCanceledException)
                return "GitHub took too long to answer; it tries again at the next check";
            if (inner is UnauthorizedAccessException)
                return "the updates folder can't be written; run this host from a folder it owns";
            if (inner is IOException && inner.Message.Contains("SHA-256", StringComparison.OrdinalIgnoreCase))
                return inner.Message;
        }
        return e.Message;
    }
}
