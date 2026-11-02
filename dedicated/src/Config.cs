namespace RenaissanceDedicated;

/// <summary>Settings shared by every server (the [defaults] table).</summary>
sealed class ServerDefaults
{
    public bool Public = true;        // public_list: list the server where players can find it
    public bool Lan = true;           // lan_address: also answer on your own network
    public int MaxPlayers = 16;
    public int? Capacity;             // engine lobby capacity; defaults to MaxPlayers
    public int TimeLimit = 12;        // minutes a game may run
    public int BigLobby = 0;          // 0 off, 64 for the big-lobby patch (writes big_lobby = 64)
    public bool FreshGameOnJoin = false; // end a game a player joined late, so they start fresh
    public int FreshGameJoinDelay = 2;   // seconds a late joiner gets before that game is ended
    public bool WaitForPlayers = true;   // hold the first game until a player has joined it
    public int LoopHz = 60;              // the engine's main loop per second; 0 is native, lower costs less CPU
    public List<string> Mods = new();
}

/// <summary>
/// The [rcon] table: a Source RCON service, one port per server, so an admin
/// panel (or any RCON client) can read a server and act on it while it runs.
/// </summary>
sealed class RconSettings
{
    public string Password = "";        // no password: RCON is off
    public string Bind = "127.0.0.1";    // where it listens; 0.0.0.0 lets another machine (or a container) reach it
    public string Address = "127.0.0.1"; // what a panel should connect to: this host doesn't use it

    public bool On => Password.Length > 0;
}

/// <summary>
/// The [announce] table: one line from the server itself into the players'
/// in-game chat, every so many minutes. It goes through the engine's own match
/// chat, so it is the same thing an owner types as `say` over RCON.
/// </summary>
sealed class AnnounceSettings
{
    public int EveryMinutes;                  // 0 (or no message) turns it off
    public List<string> Messages = new();     // said in turn, so a rotation can vary

    public bool On => EveryMinutes > 0 && Messages.Count > 0;
}

/// <summary>
/// The auto-update settings: check the game's release page for a newer build,
/// download it, and restart every server on it. Keys at the top of dedicated.toml:
/// auto_update, update_check_minutes, update_repo and update_asset.
/// </summary>
sealed class UpdateSettings
{
    public bool Auto;                          // auto_update: opt in
    public int CheckMinutes = 30;              // 0 checks once at start, then never again
    public string Repo = DefaultRepo;          // owner/name on GitHub
    public string Asset = "ReachRenaissance-*.zip";

    public const string DefaultRepo = "NETF1ix/reach-renaissance-releases";

    public bool On => Auto;
}

/// <summary>One [[server]] block: something this host runs.</summary>
sealed class ServerConfig
{
    public string Name;
    public int Port;
    public bool Enabled = true;

    // What it plays: a playlist file, or one map and game type.
    public string Playlist;           // absolute path to a .playlist.json
    public string Map;
    public string GameType;
    public string MapVariant;

    // Per-server overrides of [defaults].
    public bool? Public;
    public bool? Lan;
    public int? MaxPlayers;
    public int? Capacity;
    public int? TimeLimit;
    public bool? Upnp;
    public bool? FreshGameOnJoin;
    public int? FreshGameJoinDelay;
    public bool? WaitForPlayers;
    public int? LoopHz;
    public List<string> Mods;

    public int? AdminPort;

    // RCON: its own port by default, or none for this server.
    public int? RconPort;
    public bool? Rcon;

    public string Slug;               // data/<slug>/ for this server
    public int Line;                  // the [[server]] line, for messages

    public bool PublicOn(ServerDefaults d) => Public ?? d.Public;
    public bool LanOn(ServerDefaults d) => Lan ?? d.Lan;
    public int MaxPlayersOn(ServerDefaults d) => MaxPlayers ?? d.MaxPlayers;
    public int CapacityOn(ServerDefaults d) => Capacity ?? MaxPlayers ?? d.MaxPlayers;
    public int TimeLimitOn(ServerDefaults d) => TimeLimit ?? d.TimeLimit;
    public bool FreshOnJoin(ServerDefaults d) => FreshGameOnJoin ?? d.FreshGameOnJoin;
    public int FreshJoinDelayOn(ServerDefaults d) => FreshGameJoinDelay ?? d.FreshGameJoinDelay;
    public bool WaitForPlayersOn(ServerDefaults d) => WaitForPlayers ?? d.WaitForPlayers;
    public int LoopHzOn(ServerDefaults d) => LoopHz ?? d.LoopHz;
    public List<string> ModsOn(ServerDefaults d) => Mods ?? d.Mods;
    public int AdminPortOn() => AdminPort ?? Port + 1000;

    /// <summary>RCON shares the game's port number by default, over TCP.</summary>
    public int RconPortOn() => RconPort ?? Port;
    public bool RconOn(RconSettings rcon) => Rcon ?? rcon.On;

    /// <summary>What the server plays, for messages and status lines.</summary>
    public string Describe() =>
        !string.IsNullOrEmpty(Playlist) ? Path.GetFileName(Playlist)
        : string.IsNullOrEmpty(Map) ? "(nothing set)" : $"{Map}:{GameType}";
}

/// <summary>The dedicated.toml this host reads, resolved against its own folder.</summary>
sealed class DedicatedConfig
{
    public string ConfigPath;         // dedicated.toml
    public string Root;               // the folder holding it; relative paths start here
    public string Game;               // the Reach Renaissance folder (bin/host-probe.exe)
    public string Mcc;                // optional MCC folder; empty lets host-probe detect it
    public string Content;            // maps, game types and mods
    public string Data;               // one folder per server: config, log, status
    public string Bans;               // the ban list every server shares
    public bool Upnp;
    public string HostName = "Dedicated server";
    public string PublicAddress;      // accepted for Project Reclaimer familiarity; not used
    public string Master;             // the server list host-probe announces to
    public string Runner;             // Linux: what starts the Windows engine ("wine", Proton, a script)
    public string WinePrefix;         // the Wine prefix that runner uses, when it isn't the default
    public string ClientData;         // the ReachRenaissance folder holding fileshare\\ and playlists\\
    public string ClientDataRoot;     // resolved: the one that exists, or the usual place
    public bool RestartOnUpdate = true;   // restart servers when the game updates under them
    public UpdateSettings Update = new(); // auto_update: fetch a newer game and restart on it
    public RconSettings Rcon = new();     // the [rcon] table: an admin panel's way in
    public AnnounceSettings Announce = new(); // the [announce] table: a line in chat, on a timer
    public EngineLauncher Launcher;   // how the engine is started on this machine
    public List<string> Warnings = new();
    public ServerDefaults Defaults = new();
    public List<ServerConfig> Servers = new();

    public string HostProbe => Path.Combine(Game, "bin", "host-probe.exe");
    public string DataOf(ServerConfig s) => Path.Combine(Data, s.Slug);

    const string DefaultBans = "bans.json";
    public const string DefaultMaster = "https://2-29-30-1.sslip.io:11700";

    public static DedicatedConfig Load(string path, bool verbose = true)
    {
        string full = Path.GetFullPath(path);
        var root = Toml.ParseFile(full);
        var cfg = new DedicatedConfig
        {
            ConfigPath = full,
            Root = Path.GetDirectoryName(full) ?? ".",
        };

        var known = new HashSet<string>(StringComparer.Ordinal)
        {
            "game", "mcc", "content", "data", "bans", "upnp", "host_name", "public_address",
            "master", "defaults", "server", "runner", "wine_prefix", "client_data", "restart_on_update",
            "auto_update", "update_check_minutes", "update_repo", "update_asset",
            "rcon", "announce",
        };
        foreach (var key in root.Values.Keys)
            if (!known.Contains(key))
                cfg.Warnings.Add($"{Path.GetFileName(full)} line {Toml.LineOf(root, key)}: {key} isn't a setting this host knows; ignored");

        // "game" next to dedicated.toml is the portable layout (Project Reclaimer
        // does the same): the Reach Renaissance files are copied into it and the
        // whole folder goes to the VPS as it is. A path in the file still wins.
        cfg.Game = GameFolder.Find(Resolve(cfg.Root, Toml.Str(root, "game", full, "game")));
        cfg.Mcc = Resolve(cfg.Root, Toml.Str(root, "mcc", full, ""));
        if (string.IsNullOrEmpty(cfg.Mcc))
        {
            // MCC's own game types and maps, when the install was copied in too.
            foreach (string candidate in new[] { Path.Combine(cfg.Root, "game", "mcc"), Path.Combine(cfg.Root, "mcc") })
                if (Directory.Exists(candidate))
                {
                    cfg.Mcc = candidate;
                    break;
                }
        }
        // The engine finds a Steam install by itself, but `check` has to resolve
        // the playlist's stock game types now, so it looks in Steam's libraries too.
        if (string.IsNullOrEmpty(cfg.Mcc)) cfg.Mcc = SteamMcc.Find();
        cfg.Content = Resolve(cfg.Root, Toml.Str(root, "content", full, "content"));
        cfg.Data = Resolve(cfg.Root, Toml.Str(root, "data", full, "data"));
        cfg.Bans = Resolve(cfg.Root, Toml.Str(root, "bans", full, DefaultBans));
        cfg.Upnp = Toml.Bool(root, "upnp", full, false);
        cfg.HostName = Toml.Str(root, "host_name", full, "Dedicated server");
        cfg.PublicAddress = Toml.Str(root, "public_address", full, "");
        cfg.Runner = Toml.Str(root, "runner", full, "");
        cfg.WinePrefix = Resolve(cfg.Root, Toml.Str(root, "wine_prefix", full, ""));
        cfg.ClientData = Resolve(cfg.Root, Toml.Str(root, "client_data", full, ""));
        cfg.ClientDataRoot = ClientFolders.Find(cfg.ClientData, cfg.WinePrefix);
        cfg.RestartOnUpdate = Toml.Bool(root, "restart_on_update", full, true);

        cfg.Update.Auto = Toml.Bool(root, "auto_update", full, false);
        cfg.Update.CheckMinutes = (int)Toml.Int(root, "update_check_minutes", full, cfg.Update.CheckMinutes);
        if (cfg.Update.CheckMinutes < 0 || cfg.Update.CheckMinutes > 1440)
            throw new ConfigError(full, Toml.LineOf(root, "update_check_minutes"),
                                  $"update_check_minutes {cfg.Update.CheckMinutes} isn't 0 through 1440 " +
                                  "(0 checks once at start; 1440 is once a day)");
        cfg.Update.Repo = UpdateRepo(Toml.Str(root, "update_repo", full, cfg.Update.Repo), full,
                                     Toml.LineOf(root, "update_repo"));
        cfg.Update.Asset = Toml.Str(root, "update_asset", full, cfg.Update.Asset).Trim();
        if (cfg.Update.Asset.Length == 0 || !cfg.Update.Asset.Contains(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ConfigError(full, Toml.LineOf(root, "update_asset"),
                                  $"update_asset \"{cfg.Update.Asset}\" must name a .zip asset, such as ReachRenaissance-*.zip");

        var rcon = Toml.Table(root, "rcon", full);
        if (rcon != null)
        {
            var knownRcon = new HashSet<string>(StringComparer.Ordinal) { "password", "bind", "address" };
            foreach (var key in rcon.Values.Keys)
                if (!knownRcon.Contains(key))
                    cfg.Warnings.Add($"{Path.GetFileName(full)} line {Toml.LineOf(rcon, key)}: rcon.{key} isn't a setting this host knows; ignored");
            cfg.Rcon.Password = Toml.Str(rcon, "password", full, "");
            cfg.Rcon.Bind = Toml.Str(rcon, "bind", full, "127.0.0.1");
            cfg.Rcon.Address = Toml.Str(rcon, "address", full, "127.0.0.1");
        }
        var announce = Toml.Table(root, "announce", full);
        if (announce != null)
        {
            var knownAnnounce = new HashSet<string>(StringComparer.Ordinal) { "every_minutes", "message", "messages" };
            foreach (var key in announce.Values.Keys)
                if (!knownAnnounce.Contains(key))
                    cfg.Warnings.Add($"{Path.GetFileName(full)} line {Toml.LineOf(announce, key)}: announce.{key} isn't a setting this host knows; ignored");
            cfg.Announce.EveryMinutes = (int)Toml.Int(announce, "every_minutes", full, 10);
            if (cfg.Announce.EveryMinutes < 0 || cfg.Announce.EveryMinutes > 1440)
                throw new ConfigError(full, Toml.LineOf(announce, "every_minutes"),
                                      $"announce.every_minutes {cfg.Announce.EveryMinutes} isn't 0 through 1440 (0 turns announcements off)");
            string one = Toml.Str(announce, "message", full, "");
            if (!string.IsNullOrWhiteSpace(one)) cfg.Announce.Messages.Add(one.Trim());
            foreach (string more in Toml.StrArray(announce, "messages", full))
                if (!string.IsNullOrWhiteSpace(more)) cfg.Announce.Messages.Add(more.Trim());
        }

        if (Platform.Windows && !string.IsNullOrEmpty(cfg.Runner))
            cfg.Warnings.Add($"{Path.GetFileName(full)}: runner is for Linux; on Windows this host starts bin\\host-probe.exe itself");

        cfg.Master = Toml.Str(root, "master", full, DefaultMaster).Trim().TrimEnd('/');
        if (!Uri.TryCreate(cfg.Master, UriKind.Absolute, out var master) ||
            (master.Scheme != Uri.UriSchemeHttp && master.Scheme != Uri.UriSchemeHttps))
            throw new ConfigError(full, Toml.LineOf(root, "master"),
                                  $"master \"{cfg.Master}\" isn't an http or https address, such as {DefaultMaster}");

        var defaults = Toml.Table(root, "defaults", full);
        var def = new ServerDefaults();
        if (defaults != null)
        {
            var knownDefaults = new HashSet<string>(StringComparer.Ordinal)
            {
                "public", "lan", "max_players", "capacity", "time_limit", "big_lobby", "mods",
                "fresh_game_on_join", "fresh_game_join_delay", "wait_for_players", "loop_hz",
            };
            foreach (var key in defaults.Values.Keys)
                if (!knownDefaults.Contains(key))
                    cfg.Warnings.Add($"{Path.GetFileName(full)} line {Toml.LineOf(defaults, key)}: defaults.{key} isn't a setting this host knows; ignored");

            def.Public = Toml.Bool(defaults, "public", full, def.Public);
            def.Lan = Toml.Bool(defaults, "lan", full, def.Lan);
            def.MaxPlayers = (int)Toml.Int(defaults, "max_players", full, def.MaxPlayers);
            def.Capacity = Toml.Has(defaults, "capacity") ? (int)Toml.Int(defaults, "capacity", full, 0) : null;
            def.TimeLimit = (int)Toml.Int(defaults, "time_limit", full, def.TimeLimit);
            def.BigLobby = (int)Toml.Int(defaults, "big_lobby", full, 0);
            def.FreshGameOnJoin = Toml.Bool(defaults, "fresh_game_on_join", full, def.FreshGameOnJoin);
            def.FreshGameJoinDelay = (int)Toml.Int(defaults, "fresh_game_join_delay", full, def.FreshGameJoinDelay);
            def.WaitForPlayers = Toml.Bool(defaults, "wait_for_players", full, def.WaitForPlayers);
            def.LoopHz = (int)Toml.Int(defaults, "loop_hz", full, def.LoopHz);
            if (def.LoopHz < 0 || def.LoopHz > 1000)
                throw new ConfigError(full, Toml.LineOf(defaults, "loop_hz"),
                                      $"loop_hz {def.LoopHz} isn't 0 through 1000 (0 is the engine's native pacing)");
            def.Mods = Toml.StrArray(defaults, "mods", full);
        }
        cfg.Defaults = def;

        var blocks = Toml.TableArray(root, "server", full);
        if (blocks.Count == 0)
            cfg.Warnings.Add($"{Path.GetFileName(full)}: no [[server]] blocks, so there is nothing to run");

        var knownServer = new HashSet<string>(StringComparer.Ordinal)
        {
            "name", "port", "enabled", "playlist", "map", "game", "map_variant",
            "public", "lan", "max_players", "capacity", "time_limit", "upnp", "mods", "admin_port",
            "fresh_game_on_join", "fresh_game_join_delay", "wait_for_players", "loop_hz", "rcon", "rcon_port",
        };
        int index = 0;
        foreach (var block in blocks)
        {
            index++;
            var s = new ServerConfig { Line = block.Line };
            foreach (var key in block.Values.Keys)
                if (!knownServer.Contains(key))
                    cfg.Warnings.Add($"{Path.GetFileName(full)} line {Toml.LineOf(block, key)}: server.{key} isn't a setting this host knows; ignored");

            s.Name = Toml.Str(block, "name", full, "");
            s.Port = (int)Toml.Int(block, "port", full, 40011 + index - 1);
            s.Enabled = Toml.Bool(block, "enabled", full, true);
            s.Playlist = Resolve(cfg.Root, Toml.Str(block, "playlist", full, ""));
            s.Map = Toml.Str(block, "map", full, "");
            s.GameType = Toml.Str(block, "game", full, "");
            s.MapVariant = Resolve(cfg.Root, Toml.Str(block, "map_variant", full, ""));
            if (Toml.Has(block, "public")) s.Public = Toml.Bool(block, "public", full, true);
            if (Toml.Has(block, "lan")) s.Lan = Toml.Bool(block, "lan", full, true);
            if (Toml.Has(block, "max_players")) s.MaxPlayers = (int)Toml.Int(block, "max_players", full, 16);
            if (Toml.Has(block, "capacity")) s.Capacity = (int)Toml.Int(block, "capacity", full, 16);
            if (Toml.Has(block, "time_limit")) s.TimeLimit = (int)Toml.Int(block, "time_limit", full, 12);
            if (Toml.Has(block, "upnp")) s.Upnp = Toml.Bool(block, "upnp", full, false);
            if (Toml.Has(block, "fresh_game_on_join")) s.FreshGameOnJoin = Toml.Bool(block, "fresh_game_on_join", full, true);
            if (Toml.Has(block, "fresh_game_join_delay")) s.FreshGameJoinDelay = (int)Toml.Int(block, "fresh_game_join_delay", full, 2);
            if (Toml.Has(block, "wait_for_players")) s.WaitForPlayers = Toml.Bool(block, "wait_for_players", full, true);
            if (Toml.Has(block, "loop_hz")) s.LoopHz = (int)Toml.Int(block, "loop_hz", full, 60);
            if (Toml.Has(block, "mods")) s.Mods = Toml.StrArray(block, "mods", full);
            if (Toml.Has(block, "admin_port")) s.AdminPort = (int)Toml.Int(block, "admin_port", full, 0);
            if (Toml.Has(block, "rcon_port")) s.RconPort = (int)Toml.Int(block, "rcon_port", full, 0);
            if (Toml.Has(block, "rcon")) s.Rcon = Toml.Bool(block, "rcon", full, true);

            if (string.IsNullOrWhiteSpace(s.Name))
                throw new ConfigError(full, block.Line, $"a [[server]] block has no name (line {block.Line})");
            s.Slug = Slug(s.Name);
            cfg.Servers.Add(s);
        }

        cfg.Launcher = EngineLauncher.Resolve(cfg);
        return cfg;
    }

    /// <summary>
    /// owner/name, as GitHub names a repository. A pasted
    /// https://github.com/owner/name address says the same thing, so it is accepted.
    /// </summary>
    static string UpdateRepo(string value, string file, int line)
    {
        string repo = (value ?? "").Trim();
        foreach (string prefix in new[] { "https://github.com/", "http://github.com/", "github.com/" })
            if (repo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                repo = repo.Substring(prefix.Length);
                break;
            }
        repo = repo.Trim('/');
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        string[] parts = repo.Split('/');
        if (parts.Length != 2 || !parts.All(p => p.Length > 0 && p.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new ConfigError(file, line,
                                  $"update_repo \"{value}\" isn't owner/name, such as {UpdateSettings.DefaultRepo}");
        return repo;
    }

    /// <summary>A folder or file setting: absolute stays as-is, else it starts at the toml's folder.</summary>
    static string Resolve(string root, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string trimmed = ExpandHome(value.Trim());
        return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(root, trimmed));
    }

    /// <summary>~ is a folder name on Windows and your home folder on Linux; both are useful here.</summary>
    static string ExpandHome(string value) =>
        value == "~" ? Platform.Home
        : value.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Platform.Home, value.Substring(2))
        : value;

    /// <summary>A path as the engine must see it: Z:\\ under Wine, unchanged otherwise.</summary>
    public string EnginePath(string path) => Launcher?.AsEnginePath(path) ?? path;

    /// <summary>The same file as this host can open it, when a playlist or the engine names a Windows path.</summary>
    public string LocalPath(string path) =>
        Platform.Windows || string.IsNullOrEmpty(path) ? path : Platform.FromWindowsPath(path, WinePrefix);

    /// <summary>Reclaimer Slayer -> reclaimer-slayer, so it names a folder safely.</summary>
    public static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (c == ' ' || c == '-' || c == '_' || c == '.') sb.Append('-');
        }
        string slug = sb.ToString().Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "server" : slug;
    }
}

/// <summary>
/// The folder holding the game's engine: bin\host-probe.exe (Windows) or
/// bin/host-probe (native). dedicated.toml's game points at it, and its default
/// is "game" next to the file, so a VPS install is a folder you copy and nothing
/// more. A release unzipped inside it (game/ReachRenaissance/bin/...) is found
/// too, so both shapes of the same thing work.
/// </summary>
static class GameFolder
{
    public static string Find(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return folder;
        if (Holds(folder)) return folder;
        try
        {
            foreach (string sub in Directory.GetDirectories(folder))
                if (Holds(sub)) return sub;
            foreach (string sub in Directory.GetDirectories(folder))
                foreach (string inner in Directory.GetDirectories(sub))
                    if (Holds(inner)) return inner;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return folder;
    }

    static bool Holds(string dir) =>
        File.Exists(Path.Combine(dir, "bin", "host-probe.exe")) ||
        File.Exists(Path.Combine(dir, "bin", "host-probe"));

    /// <summary>Is this the portable game folder, rather than a path in the file?</summary>
    public static bool IsPortable(DedicatedConfig cfg) =>
        string.Equals(cfg.Game, Path.Combine(cfg.Root, "game"), StringComparison.OrdinalIgnoreCase) ||
        cfg.Game.StartsWith(Path.Combine(cfg.Root, "game") + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase);
}
