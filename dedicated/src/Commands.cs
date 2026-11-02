using System.Text;
using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>The check command: everything that would stop a server starting.</summary>
static class CheckCommand
{
    public static int Print(DedicatedConfig cfg)
    {
        int problems = 0, warnings = 0;

        foreach (string warning in cfg.Warnings)
        {
            Console.WriteLine("warning: " + warning);
            warnings++;
        }

        Console.WriteLine($"config   {cfg.ConfigPath}");
        Console.WriteLine($"game     {cfg.Game}" + (GameFolder.IsPortable(cfg) ? "   (game/, next to dedicated.toml)" : ""));
        if (!Directory.Exists(cfg.Game))
        {
            Error($"the game folder isn't there: {cfg.Game}. Copy the Reach Renaissance files into it " +
                  "(bin, data, themes, licenses, VERSION.txt; the launcher .exe isn't needed).");
        }
        else if (!File.Exists(cfg.HostProbe))
        {
            Error($"{cfg.HostProbe} isn't there. Copy the Reach Renaissance files into {cfg.Game} " +
                  "(bin, data, themes, licenses, VERSION.txt), or point game in dedicated.toml at the folder " +
                  "holding bin\\host-probe.exe.");
        }
        else
        {
            string version = Path.Combine(cfg.Game, "VERSION.txt");
            Console.WriteLine($"         {cfg.HostProbe}");
            if (File.Exists(version))
                Console.WriteLine($"         {File.ReadAllLines(version).FirstOrDefault()?.Trim()}");
        }

        if (!string.IsNullOrEmpty(cfg.Mcc))
        {
            string dll = Path.Combine(cfg.Mcc, "haloreach", "haloreach.dll");
            if (!File.Exists(dll))
                Error($"mcc is set, but {dll} isn't there");
            else
                Console.WriteLine($"mcc      {cfg.Mcc}");
        }
        else
        {
            Console.WriteLine("mcc      not set; host-probe finds your Steam install itself");
        }

        if (cfg.Launcher?.Problem != null)
            Error(cfg.Launcher.Problem);
        else if (cfg.Launcher != null)
            Console.WriteLine($"engine   {cfg.Launcher.Describe()}");

        if (cfg.Servers.Any(s => s.Enabled && s.PublicOn(cfg.Defaults)))
            Console.WriteLine($"master   {cfg.Master}   (servers with public = true register here)");
        else
            Console.WriteLine("master   not used: every server is private (public = false)");

        if (!Directory.Exists(cfg.Content))
        {
            Console.WriteLine($"content  {cfg.Content} (not there yet; the host makes it on the first start)");
        }
        else
        {
            var contents = new NativeContents(cfg);
            Console.WriteLine($"content  {cfg.Content} ({contents.ListGameTypes().Count} game types, " +
                              $"{contents.ListMaps().Count} saved maps, {contents.ListMods().Count} mods)");
        }

        if (!string.IsNullOrEmpty(cfg.PublicAddress))
        {
            Console.WriteLine("warning: public_address is set, but Reach Renaissance lists the address its server lists " +
                              "see for themselves; this host doesn't write it anywhere");
            warnings++;
        }

        foreach (var group in cfg.Servers.Where(s => s.Enabled).GroupBy(s => s.Port))
            if (group.Count() > 1)
                Error($"{string.Join(" and ", group.Select(s => s.Name))} share port {group.Key}; every server needs its own");

        foreach (var group in cfg.Servers.Where(s => s.Enabled).GroupBy(s => s.AdminPortOn()))
            if (group.Count() > 1)
                Error($"{string.Join(" and ", group.Select(s => s.Name))} share admin port {group.Key}; set admin_port on one of them");

        Console.WriteLine();
        var prepared = Host.PrepareAll(cfg, write: false);
        foreach (var p in prepared)
        {
            if (p.Ok)
            {
                Console.WriteLine($"{p.Config.Name,-24} ready   udp {p.Config.Port}   " +
                                  $"{p.Games.Count} game(s)   plays {p.Config.Describe()}");
                // A panel needs to be told where RCON is; say it here, where the rest
                // of the setup is being checked.
                string rcon = p.Config.RconOn(cfg.Rcon)
                    ? $"on tcp {cfg.Rcon.Bind}:{p.Config.RconPortOn()}   (the [rcon] password; a panel connects to {cfg.Rcon.Address})"
                    : "off: set a [rcon] password in dedicated.toml to run this server from a panel";
                Console.WriteLine($"{new string(' ', 24)} rcon {rcon}");
                foreach (string note in p.Notes)
                {
                    Console.WriteLine($"{new string(' ', 24)} note: {note}");
                    warnings++;
                }
            }
            else
            {
                foreach (string problem in p.Problems)
                    Error($"{p.Config.Name}: {problem}");
                foreach (string note in p.Notes)
                    Console.WriteLine($"{new string(' ', 24)} note: {note}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{prepared.Count(p => p.Ok)} of {prepared.Count} servers ready" +
                          (warnings > 0 ? $", {warnings} warning(s)" : "") + ".");
        if (cfg.Servers.Any(s => s.Enabled && s.PublicOn(cfg.Defaults)))
            Console.WriteLine("reach: run \"reach\" to ask the server list whether players reach these directly " +
                              "or through its relay.");
        return problems == 0 && cfg.Servers.Any(s => s.Enabled) ? 0 : 2;

        void Error(string message)
        {
            Console.Error.WriteLine("error: " + message);
            problems++;
        }
    }
}

/// <summary>The content command: what a playlist can name, and where it lives.</summary>
static class ContentCommand
{
    public static int Print(DedicatedConfig cfg, List<string> words = null)
    {
        var contents = new NativeContents(cfg);

        Console.WriteLine($"Game types (game = \".bin\" file or a name the game knows):");
        Print(Filter(contents.ListGameTypes(), words), Path.Combine(cfg.Content, "Game Modes"), "(none yet)");
        Console.WriteLine();
        Console.WriteLine($"Saved Forge maps (map_variant =):");
        Print(Filter(contents.ListMaps(), words), Path.Combine(cfg.Content, "Maps"), "(none yet)");
        Console.WriteLine();
        Console.WriteLine("Mods (mods = [...] in dedicated.toml, one folder each):");
        var mods = contents.ListMods();
        if (mods.Count == 0) Console.WriteLine("  (none yet)");
        foreach (string mod in mods)
        {
            string map = Directory.GetFiles(mod, "*.map").FirstOrDefault();
            string mapinfo = map == null ? null : Directory.GetFiles(mod, Path.GetFileNameWithoutExtension(map) + ".mapinfo").FirstOrDefault();
            Console.WriteLine($"  {Path.GetFileName(mod),-32} {Path.GetFileName(map) ?? "no .map"}" +
                              (mapinfo == null ? "  (no .mapinfo; the engine needs one)" : ""));
        }
        Console.WriteLine();

        Console.WriteLine("The client's own fileshare (maps and game types you saved in the menus):");
        Print(new List<string>(SafeFiles(contents.FileshareGameType, "*.bin")
                .Concat(SafeFiles(contents.FileshareMap, "*.mvar"))), contents.FileshareRoot, "(nothing saved yet)");
        Console.WriteLine();

        Console.WriteLine("Playlists the menus wrote (playlist import <file> turns one into a playlist.json):");
        var menus = SafeFiles(contents.MenusPlaylists, "*.txt");
        if (menus.Count == 0) Console.WriteLine("  (none yet)");
        foreach (string p in menus) Console.WriteLine("  " + p);
        Console.WriteLine();

        if (string.IsNullOrEmpty(cfg.Mcc))
            Console.WriteLine("Set mcc in dedicated.toml to list Halo: Reach's own game variants from your install too.");
        else
        {
            Console.WriteLine("MCC's own Reach game variants:");
            Print(SafeFiles(Path.Combine(cfg.Mcc, "haloreach", "hopper_game_variants"), "*.bin"),
                  Path.Combine(cfg.Mcc, "haloreach", "hopper_game_variants"), "(none found)");
        }

        Console.WriteLine();
        Console.WriteLine("A playlist entry names a map and a game: {\"map\": \"ForgeWorld\", \"game\": \"Slayer\"} or");
        Console.WriteLine("{\"map\": \"ForgeWorld\", \"game\": \"Game Modes/myvariant.bin\", \"map_variant\": \"Maps/mymap.mvar\"}.");
        Console.WriteLine("A name without a path is left for the game to resolve, such as ForgeWorld or Swordbase.");
        return 0;
    }

    /// <summary>content <words> only lists files whose name holds one of the words.</summary>
    static List<string> Filter(List<string> files, List<string> words) =>
        words == null || words.Count == 0
            ? files
            : files.Where(f => words.Any(w => Path.GetFileName(f).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();

    static void Print(List<string> files, string folder, string empty)
    {
        if (files.Count == 0)
        {
            Console.WriteLine($"  {empty}   ({folder})");
            return;
        }
        foreach (string file in files)
            Console.WriteLine($"  {Path.GetFileName(file),-44} {file}");
    }

    static List<string> SafeFiles(string dir, string pattern) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).OrderBy(f => f).ToList() : new List<string>();
}

/// <summary>show, import and new for the playlist.json files.</summary>
static class PlaylistCommand
{
    public static int Show(string path)
    {
        var playlist = Playlist.Load(path);
        Console.WriteLine($"{Path.GetFileName(playlist.Path)}");
        Console.WriteLine($"  name         {playlist.Name}");
        Console.WriteLine($"  order        {playlist.Order}");
        Console.WriteLine($"  map vote     {(playlist.Vote ? $"on, {playlist.VoteChoices} games, {playlist.VoteTime} s" : "off")}");
        Console.WriteLine($"  games        {playlist.Games.Count}");
        foreach (var game in playlist.Games)
            Console.WriteLine($"    {game.Map}:{game.Game}" +
                              (string.IsNullOrEmpty(game.MapVariant) ? "" : "|" + game.MapVariant) +
                              (game.Weight > 1 ? $"  (weight {game.Weight})" : ""));
        foreach (string warning in playlist.Warnings) Console.WriteLine("  warning: " + warning);
        return 0;
    }

    /// <summary>
    /// Turns a playlist the Reach Renaissance menus wrote (playlists/*.txt) into a
    /// playlist.json this host reads. Paths it names are kept as they are.
    /// </summary>
    public static int Import(string nativePath, string outPath)
    {
        if (!File.Exists(nativePath))
        {
            Console.Error.WriteLine($"error: {nativePath} isn't there");
            return 2;
        }

        var playlist = new Playlist { Path = Path.GetFullPath(nativePath) };
        PlaylistEntry last = null;
        int lineNumber = 0, ignored = 0;
        foreach (string rawLine in File.ReadAllLines(nativePath))
        {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                Console.Error.WriteLine($"  line {lineNumber}: not key = value, left out: {line}");
                ignored++;
                continue;
            }
            string key = line.Substring(0, equals).Trim().ToLowerInvariant();
            string value = line.Substring(equals + 1).Trim();
            switch (key)
            {
                case "name": playlist.Name = value; break;
                case "order": playlist.Order = value.ToLowerInvariant(); break;
                case "vote": playlist.Vote = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase); break;
                case "vote_choices": playlist.VoteChoices = ParseInt(value, playlist.VoteChoices); break;
                case "vote_time": playlist.VoteTime = ParseInt(value, playlist.VoteTime); break;
                case "game":
                {
                    int colon = value.IndexOf(':');
                    if (colon < 0)
                    {
                        Console.Error.WriteLine($"  line {lineNumber}: a game is Map:game type, left out: {value}");
                        ignored++;
                        last = null;
                        break;
                    }
                    last = new PlaylistEntry
                    {
                        Map = value.Substring(0, colon).Trim(),
                        Game = value.Substring(colon + 1).Trim(),
                    };
                    playlist.Games.Add(last);
                    break;
                }
                case "map_variant":
                    if (last == null)
                    {
                        Console.Error.WriteLine($"  line {lineNumber}: map_variant without a game before it, left out");
                        ignored++;
                    }
                    else
                    {
                        last.MapVariant = value;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"  line {lineNumber}: {key} isn't part of a playlist, left out ({value})");
                    ignored++;
                    break;
            }
        }

        if (playlist.Games.Count == 0)
        {
            Console.Error.WriteLine("error: no games found in that file");
            return 2;
        }

        if (string.IsNullOrEmpty(outPath))
            outPath = Path.Combine(Path.GetDirectoryName(playlist.Path) ?? ".", playlist.Name + ".playlist.json");
        Write(playlist, outPath);
        Console.WriteLine($"imported {playlist.Games.Count} game(s) from {Path.GetFileName(nativePath)} to {outPath}" +
                          (ignored > 0 ? $", {ignored} line(s) left out" : ""));
        return 0;
    }

    /// <summary>A new playlist: a map list crossed with a game list, as Reclaimer's does.</summary>
    public static int New(string name, string maps, string games, string outPath)
    {
        var playlist = new Playlist { Path = outPath ?? name + ".playlist.json", Name = name };
        var mapList = Split(maps);
        var gameList = Split(games);
        if (mapList.Count == 0 || gameList.Count == 0)
        {
            Console.Error.WriteLine("error: pass --maps and --games, for example --maps \"ForgeWorld\" --games \"Slayer\"");
            return 2;
        }
        foreach (string map in mapList)
            foreach (string game in gameList)
                playlist.Games.Add(new PlaylistEntry { Map = map, Game = game });

        Write(playlist, playlist.Path);
        Console.WriteLine($"wrote {playlist.Path} with {playlist.Games.Count} game(s)");
        return 0;
    }

    static void Write(Playlist playlist, string path)
    {
        var json = new Dictionary<string, object>
        {
            ["version"] = 1,
            ["name"] = playlist.Name,
            ["order"] = playlist.Order,
            ["vote"] = playlist.Vote,
            ["vote_choices"] = playlist.VoteChoices,
            ["vote_time"] = playlist.VoteTime,
        };
        var games = new List<Dictionary<string, object>>();
        foreach (var game in playlist.Games)
        {
            var entry = new Dictionary<string, object> { ["map"] = game.Map, ["game"] = game.Game };
            if (!string.IsNullOrEmpty(game.MapVariant)) entry["map_variant"] = game.MapVariant;
            if (game.Weight > 1) entry["weight"] = game.Weight;
            games.Add(entry);
        }
        json["games"] = games;

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    static List<string> Split(string value) =>
        string.IsNullOrWhiteSpace(value) ? new List<string>()
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

    static int ParseInt(string value, int fallback) => int.TryParse(value, out int parsed) ? parsed : fallback;
}
