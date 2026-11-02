using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace RenaissanceDedicated;

/// <summary>
/// What this machine is, and how the engine's Windows paths relate to it. Reach
/// Renaissance is a Windows program: on Linux the engine runs through Wine (or
/// Proton), where every Linux path is the Z: drive.
/// </summary>
static class Platform
{
    public static bool Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string Home =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home
            ? home
            : Environment.GetEnvironmentVariable("HOME") ?? ".";

    /// <summary>A Windows path a Windows program would understand, such as C:\games or Z:\home\me.</summary>
    public static bool IsWindowsPath(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (value.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
    }

    /// <summary>The path as Wine shows it to a Windows program: Z: for the root, backslashes.</summary>
    public static string ToWindowsPath(string path)
    {
        if (IsWindowsPath(path)) return path.Replace('/', '\\');
        return "Z:" + path.Replace('/', '\\');
    }

    /// <summary>
    /// The path behind a Windows path the engine or a playlist named: Z: is the
    /// root, C: (and any other drive) is inside the Wine prefix.
    /// </summary>
    public static string FromWindowsPath(string value, string winePrefix)
    {
        if (!IsWindowsPath(value) || value.StartsWith(@"\\", StringComparison.Ordinal)) return value;
        char drive = char.ToLowerInvariant(value[0]);
        string rest = value.Substring(2).TrimStart('\\', '/').Replace('\\', '/');
        if (drive == 'z') return "/" + rest;
        // The answer is a Linux path, so it is built with Linux separators whatever
        // machine this host itself runs on.
        if (drive == 'c' && !string.IsNullOrWhiteSpace(winePrefix))
            return winePrefix.TrimEnd('/', '\\') + "/drive_c/" + rest;
        return value;
    }
}

/// <summary>
/// Where the Reach Renaissance client keeps your name, settings and the game
/// types, maps and playlists the menus wrote: %LOCALAPPDATA%\ReachRenaissance on
/// Windows, inside the Wine or Proton prefix on Linux.
/// </summary>
static class ClientFolders
{
    /// <summary>The folder holding fileshare\ and playlists\, or the usual place when none exists yet.</summary>
    public static string Find(string configured, string winePrefix)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var candidates = Candidates(winePrefix);
        foreach (string candidate in candidates)
            if (Directory.Exists(candidate))
                return candidate;
        return candidates.Count > 0 ? candidates[0] : "";
    }

    static List<string> Candidates(string winePrefix)
    {
        var list = new List<string>();
        if (Platform.Windows)
        {
            list.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReachRenaissance"));
            return list;
        }

        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome)) dataHome = Path.Combine(Platform.Home, ".local", "share");
        list.Add(Path.Combine(dataHome, "ReachRenaissance"));

        string prefix = winePrefix;
        if (string.IsNullOrWhiteSpace(prefix)) prefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        if (string.IsNullOrWhiteSpace(prefix)) prefix = Path.Combine(Platform.Home, ".wine");
        AddPrefix(list, prefix);

        // Proton keeps a prefix per game: MCC is 976730, and a non-Steam shortcut
        // (how the game's own README suggests adding Reach Renaissance) gets a
        // number of its own. The newest one that holds the client's folders wins.
        var found = new List<(DateTime When, string Path)>();
        foreach (string steam in SteamFolders())
        {
            string compatdata = Path.Combine(steam, "steamapps", "compatdata");
            if (!Directory.Exists(compatdata)) continue;
            foreach (string candidates in PrefixesUnder(compatdata))
            {
                string folder = ClientFolder(candidates);
                if (folder == null) continue;
                try
                {
                    found.Add((Directory.GetLastWriteTimeUtc(folder), folder));
                }
                catch
                {
                    // A prefix that can't be read is one we can't use.
                }
            }
        }
        found.Sort((a, b) => b.When.CompareTo(a.When));
        list.AddRange(found.Select(f => f.Path));
        return list;
    }

    static void AddPrefix(List<string> list, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;
        string users = Path.Combine(prefix, "drive_c", "users");
        if (!Directory.Exists(users))
        {
            list.Add(Path.Combine(prefix, "drive_c", "users", "steamuser", "AppData", "Local", "ReachRenaissance"));
            return;
        }
        try
        {
            foreach (string user in Directory.GetDirectories(users))
                list.Add(ClientFolder(user) ?? Path.Combine(user, "AppData", "Local", "ReachRenaissance"));
        }
        catch
        {
            // Same: unreadable prefixes come and go.
        }
    }

    static string ClientFolder(string userFolder)
    {
        string folder = Path.Combine(userFolder, "AppData", "Local", "ReachRenaissance");
        return Directory.Exists(folder) ? folder : null;
    }

    static IEnumerable<string> PrefixesUnder(string compatdata)
    {
        try
        {
            foreach (string app in Directory.GetDirectories(compatdata))
            {
                string users = Path.Combine(app, "pfx", "drive_c", "users");
                if (Directory.Exists(users)) yield return users;
            }
        }
        finally
        {
            // Nothing to clean up: the iterator is the whole point.
        }
    }

    internal static IEnumerable<string> SteamFolders()
    {
        yield return Path.Combine(Platform.Home, ".steam", "steam");
        yield return Path.Combine(Platform.Home, ".steam", "root");
        yield return Path.Combine(Platform.Home, ".local", "share", "Steam");
        yield return Path.Combine(Platform.Home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam");
    }
}

/// <summary>
/// Where Halo: The Master Chief Collection is installed, found the same way the
/// engine finds it: from Steam's own libraries. Without this, `check` reports
/// every stock game type as missing on a PC that has MCC but no `mcc` line in
/// dedicated.toml, even though the server would start and find it.
/// </summary>
static class SteamMcc
{
    public static string Find()
    {
        foreach (string root in Roots())
            foreach (string library in Libraries(root))
            {
                string mcc = Path.Combine(library, "steamapps", "common", "Halo The Master Chief Collection");
                if (File.Exists(Path.Combine(mcc, "haloreach", "haloreach.dll"))) return mcc;
            }
        return "";
    }

    static IEnumerable<string> Roots()
    {
        if (Platform.Windows)
        {
            string x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(x86)) yield return Path.Combine(x86, "Steam");
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(programFiles)) yield return Path.Combine(programFiles, "Steam");
        }
        else
        {
            foreach (string steam in ClientFolders.SteamFolders()) yield return steam;
        }
    }

    static IEnumerable<string> Libraries(string root)
    {
        if (Directory.Exists(root)) yield return root;

        string file = Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(file)) yield break;
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            yield break;
        }

        foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\""))
        {
            string path = match.Groups[1].Value.Replace("\\\\", "\\");
            if (Directory.Exists(path)) yield return path;
        }
    }
}
