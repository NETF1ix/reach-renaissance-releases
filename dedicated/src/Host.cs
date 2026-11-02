using System.Diagnostics;
using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>One server, with the native files it needs already worked out.</summary>
sealed class PreparedServer
{
    public ServerConfig Config;
    public string Dir;
    public string IniPath;
    public string PlaylistName;          // the native playlist name, or null
    public Playlist Playlist;
    public List<ResolvedEntry> Games = new();
    public List<string> Problems = new();
    public List<string> Notes = new();
    public bool Ok => Problems.Count == 0;
    public int AdminPort => Config.AdminPortOn();
}

/// <summary>
/// Writes each server's native config and runs its host-probe process: one
/// Reach Renaissance dedicated server per [[server]] block.
/// </summary>
static class Host
{
    public static PreparedServer Prepare(DedicatedConfig cfg, ServerConfig server, bool write)
    {
        var p = new PreparedServer { Config = server, Dir = cfg.DataOf(server) };
        p.IniPath = Path.Combine(p.Dir, "server.ini");

        if (server.Port < 1 || server.Port > 65535)
            p.Problems.Add($"port {server.Port} isn't from 1 through 65535");
        if (server.AdminPortOn() < 1 || server.AdminPortOn() > 65535)
            p.Problems.Add($"admin_port {server.AdminPortOn()} isn't from 1 through 65535");
        if (server.MaxPlayersOn(cfg.Defaults) < 1 || server.MaxPlayersOn(cfg.Defaults) > 64)
            p.Problems.Add($"max_players must be 1 through 64 (Reach Renaissance's cap with big_lobby), not {server.MaxPlayersOn(cfg.Defaults)}");
        if (server.MaxPlayersOn(cfg.Defaults) > 32 && cfg.Defaults.BigLobby != 64)
            p.Problems.Add($"max_players {server.MaxPlayersOn(cfg.Defaults)} needs big_lobby = 64 in [defaults]");
        if (server.TimeLimitOn(cfg.Defaults) < 0)
            p.Problems.Add("time_limit can't be negative");
        if (server.LoopHzOn(cfg.Defaults) < 0 || server.LoopHzOn(cfg.Defaults) > 1000)
            p.Problems.Add($"loop_hz {server.LoopHzOn(cfg.Defaults)} isn't 0 through 1000 (0 is the engine's native pacing)");
        if (cfg.Launcher?.Problem != null)
            p.Problems.Add(cfg.Launcher.Problem);

        var contents = new NativeContents(cfg);

        if (!string.IsNullOrEmpty(server.Playlist))
        {
            if (!File.Exists(server.Playlist))
            {
                p.Problems.Add($"playlist {server.Playlist} isn't there");
            }
            else
            {
                p.Playlist = Playlist.Load(server.Playlist);
                if (!string.IsNullOrEmpty(server.Map) || !string.IsNullOrEmpty(server.GameType))
                    p.Notes.Add("the playlist is used; map and game are ignored for this server");
                foreach (var w in p.Playlist.Warnings) p.Notes.Add(w);
                if (p.Playlist.VoteTime < 5 || p.Playlist.VoteTime > 120)
                    p.Notes.Add($"vote_time {p.Playlist.VoteTime} is unusual; the dashboard wants seconds a vote stays open");
            }
        }
        else if (string.IsNullOrEmpty(server.Map) || string.IsNullOrEmpty(server.GameType))
        {
            p.Problems.Add("give the server a playlist, or a map and a game");
        }

        if (p.Playlist != null)
        {
            p.PlaylistName = Native.FreePlaylistName(p.Playlist.Name, contents.MenusPlaylists);
            if (p.PlaylistName != Native.Name(p.Playlist.Name))
                p.Notes.Add($"the playlist goes into the server folder as \"{p.PlaylistName}\": the game's menus already have a " +
                            $"playlist called \"{Native.Name(p.Playlist.Name)}\", and a name held by both is ambiguous");
        }

        p.Games = contents.ResolveGames(server, p.Playlist, p.Problems);
        if (p.Games.Count == 0 && p.Problems.Count == 0)
            p.Problems.Add("no game in this server could be resolved");

        // A game type named without a path only works if that .bin is on this PC.
        // The game skips those entries and refuses to start when none is left.
        var named = p.Playlist != null
            ? p.Playlist.Games.Select(g => g.Game)
            : new List<string> { server.GameType };
        foreach (string spec in named.Where(g => !string.IsNullOrEmpty(g) && !NativeContents.LooksLikeAFile(g)).Distinct())
            if (!contents.GameTypeNameExists(spec))
                p.Notes.Add($"game type \"{spec}\": no file with that name is on this PC, and the game looks for " +
                            $"{contents.GameTypeLookupHint(spec)}; name a .bin instead " +
                            "(content/Game Modes, your fileshare's GameType folder, or MCC's own variants)");
        if (p.Games.Any(g => g.Weight > 1))
            p.Notes.Add("weight is written as that many copies of the game, which is what a shuffle picks from");

        foreach (string mod in server.ModsOn(cfg.Defaults))
        {
            string resolved = ResolveMod(cfg, mod);
            if (resolved == null)
                p.Problems.Add($"mod \"{mod}\" wasn't found; look in {Path.Combine(cfg.Content, "Mods")}");
            else if (Directory.GetFiles(resolved, "*.map").Length == 0)
                p.Notes.Add($"mod {Path.GetFileName(resolved)} has no .map file, so the engine may refuse it");
        }

        if (write && p.Ok)
        {
            Directory.CreateDirectory(p.Dir);
            File.WriteAllText(p.IniPath, Native.ServerIni(server, cfg, p.Games, p.PlaylistName));
            if (p.Playlist != null)
            {
                string playlistDir = Path.Combine(p.Dir, "playlists");
                Directory.CreateDirectory(playlistDir);
                File.WriteAllText(Path.Combine(playlistDir, p.PlaylistName + ".txt"), Native.PlaylistText(p.Playlist, p.Games));
            }
        }

        return p;
    }

    public static List<PreparedServer> PrepareAll(DedicatedConfig cfg, bool write)
    {
        var list = new List<PreparedServer>();
        foreach (var server in cfg.Servers)
        {
            if (!server.Enabled) continue;
            try
            {
                list.Add(Prepare(cfg, server, write));
            }
            catch (ConfigError e)
            {
                // One server's playlist being wrong shouldn't hide the others.
                var failed = new PreparedServer { Config = server, Dir = cfg.DataOf(server) };
                failed.IniPath = Path.Combine(failed.Dir, "server.ini");
                failed.Problems.Add(e.Line > 0
                    ? $"{Path.GetFileName(e.File)} line {e.Line}: {e.Message}"
                    : $"{Path.GetFileName(e.File)}: {e.Message}");
                list.Add(failed);
            }
        }
        return list;
    }

    /// <summary>The command line for one server, as this host runs it (for --dry-run and logs).</summary>
    public static string CommandLine(PreparedServer p, DedicatedConfig cfg) =>
        cfg.Launcher.CommandLine(Arguments(p, cfg));

    /// <summary>
    /// What one engine is started with, in one place so a dry run and a real start
    /// can't drift apart. Paths are handed over as the engine sees them, which under
    /// Wine is Z:\; quoting for display happens in the launcher, not here.
    /// </summary>
    public static List<string> Arguments(PreparedServer p, DedicatedConfig cfg)
    {
        string E(string path) => cfg.EnginePath(path);
        var args = new List<string>
        {
            E(p.Dir),
            "--dedicated",
            "--config", E(p.IniPath),
            "--hidden",
            "--no-render",
            "--admin-dir", E(Path.Combine(p.Dir, "admin")),
            "--ban-list", E(cfg.Bans),
            "--admin-port", p.AdminPort.ToString(),
        };
        // The engine only registers with a server list when it is told to announce;
        // the server.ini's public_list marks the listing but never registers it.
        if (p.Config.PublicOn(cfg.Defaults))
        {
            args.Add("--announce");
            args.Add(cfg.Master);
            args.Add("--max-listed-players");
            args.Add(p.Config.MaxPlayersOn(cfg.Defaults).ToString());
        }
        // A game that starts with nobody in it is one nobody can join: the engine
        // admits a late player, sends them the map, and never spawns them (black
        // screen, DEAD on the scoreboard). --expect-members is the engine's own
        // launch hold: "host waits for N members" before it starts the match. The
        // count includes the host's own player, so 2 means "wait for one player".
        // The hold gives up after about 45 s, so an empty server still cycles.
        if (p.Config.WaitForPlayersOn(cfg.Defaults))
        {
            args.Add("--expect-members");
            args.Add("2");
        }
        // The game's own launcher always passes this: a server list on your own
        // network answers over plain http, and that is allowed here.
        args.Add("--allow-insecure-http");
        if (!string.IsNullOrEmpty(cfg.Mcc))
        {
            args.Add("--mcc");
            args.Add(E(cfg.Mcc));
        }
        // Reach Renaissance's client loads the Elite armor pack by default, and its
        // membership checksum covers the resulting player representation. The server
        // engine has to load the same pack with the same feature flags: without them
        // a joining client keeps reporting "membership checksum mismatch", asks for a
        // full update, and its join times out before it spawns (the black screen /
        // permanently-dead symptom). These are the same flags the game's client uses.
        string armor = Path.Combine(cfg.Game ?? "", "data", "armor");
        if (Directory.Exists(armor) && File.Exists(Path.Combine(armor, "rr_elite_pack.map")))
        {
            args.Add("--extra-maps");
            args.Add(E(armor));
            args.Add("--armor-pack");
            args.Add("rr_elite_pack");
            args.Add("--elite-armor-slots");
        }
        string strings = Path.Combine(cfg.Game ?? "", "data", "strings_en.json");
        if (File.Exists(strings))
        {
            args.Add("--strings");
            args.Add(E(strings));
        }
        args.Add("--elite-looks");
        args.Add("--features-on");
        args.Add("6e");
        foreach (string mod in p.Config.ModsOn(cfg.Defaults))
        {
            string resolved = ResolveMod(cfg, mod);
            if (resolved == null) continue;
            args.Add("--mod");
            args.Add(E(resolved));
        }
        return args;
    }

    static string ResolveMod(DedicatedConfig cfg, string mod)
    {
        if (Path.IsPathRooted(mod)) return Directory.Exists(mod) ? mod : null;
        string direct = Path.GetFullPath(Path.Combine(cfg.Root, mod));
        if (Directory.Exists(direct)) return direct;
        string inMods = Path.Combine(cfg.Content, "Mods", mod);
        if (Directory.Exists(inMods)) return inMods;
        return null;
    }

    /// <summary>Runs every enabled server until Ctrl+C. Returns the process exit code.</summary>
    public static int Start(DedicatedConfig cfg, bool dryRun, bool verbose)
    {
        Ui.Begin(verbose);
        Ui.Line($"project-renaissance-dedicated {Version()} - Reach Renaissance dedicated servers");
        Ui.Line($"config     {cfg.ConfigPath}");
        string gameVersion = GameBuild.Version(cfg);
        Ui.Line($"game       {cfg.Game}" + (gameVersion.Length > 0 ? $"   ({gameVersion})" : ""));
        if (!Platform.Windows) Ui.Line($"engine     {cfg.Launcher?.Describe()}");
        if (!string.IsNullOrEmpty(cfg.Mcc)) Ui.Line($"mcc        {cfg.Mcc}");
        foreach (string warning in cfg.Warnings)
            Ui.Line("warning: " + warning);
        Ui.Line("");

        if (!string.IsNullOrEmpty(cfg.Launcher?.Problem))
        {
            Ui.Line("error: " + cfg.Launcher.Problem);
            Ui.End();
            return 2;
        }

        var prepared = PrepareAll(cfg, write: !dryRun);
        if (prepared.Count == 0)
        {
            Ui.Line("error: no servers are enabled in dedicated.toml");
            Ui.End();
            return 2;
        }

        var running = new List<RunningServer>();
        void LaunchAll()
        {
            foreach (var p in prepared)
            {
                if (!p.Ok)
                {
                    Ui.Line($"[{p.Config.Name}] not started:");
                    foreach (string problem in p.Problems) Ui.Line("    " + problem);
                    continue;
                }
                foreach (string note in p.Notes) Ui.Line($"[{p.Config.Name}] note: {note}");

                if (dryRun)
                {
                    Ui.Line($"[{p.Config.Name}] would run: {CommandLine(p, cfg)}");
                    Ui.Line($"[{p.Config.Name}] server.ini:  {p.IniPath}");
                    continue;
                }

                var server = new RunningServer(p, cfg, () => ReloadAll(cfg, running, dryRun));
                if (server.Start())
                    running.Add(server);
            }
        }
        LaunchAll();

        if (dryRun)
        {
            Ui.Line("");
            Ui.Line($"{prepared.Count(p => p.Ok)} of {prepared.Count} servers would start.");
            Ui.End();
            return prepared.All(p => p.Ok) ? 0 : 2;
        }

        if (running.Count == 0)
        {
            Ui.Line("error: nothing could start; fix the problems above (dedicated.toml check says more)");
            Ui.End();
            return 2;
        }

        Ui.Line("");
        Ui.Line("Ctrl+C stops every server. The engine's own output goes to each server's log" +
                (verbose ? " and to this window." : "."));
        Ui.Line("");

        bool stopping = false;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (stopping) return;
            stopping = true;
            Ui.Line(Environment.NewLine + "stopping servers...");
        };

        // A server must not keep running an engine the game has replaced: the newest
        // build is what players are on, and the two versions can't play together.
        string gameStamp = GameBuild.Stamp(cfg);
        int ticks = 0;

        // Auto-update: check the game's release page, download and verify a newer
        // build in the background, then stop the servers only for the file swap.
        // The first check is soon after start so a VPS left alone catches up at once.
        Task<UpdateCheck> updateTask = null;
        DateTime nextUpdateCheck = DateTime.UtcNow.AddSeconds(20);
        string updateNote = null;
        void UpdateLog(string text) => Ui.Line($"{DateTime.Now:HH:mm:ss}  {text}");
        void UpdateOnce(string text)
        {
            if (text == null || text == updateNote) return;
            updateNote = text;
            UpdateLog(text);
        }
        if (cfg.Update.On && !dryRun)
        {
            string every = cfg.Update.CheckMinutes switch
            {
                0 => "on start only",
                1 => "every minute",
                _ => $"every {cfg.Update.CheckMinutes} minutes",
            };
            string installedGame = GameBuild.Version(cfg);
            Ui.Line($"auto-update  {every} from {cfg.Update.Repo} " +
                    $"(installed {(installedGame.Length > 0 ? installedGame : "none")})");
        }

        try
        {
            while (!stopping)
            {
                Thread.Sleep(1000);
                ticks++;

                if (cfg.Update.On && !dryRun)
                {
                    if (updateTask == null && DateTime.UtcNow >= nextUpdateCheck)
                    {
                        updateTask = Task.Run(() => Updater.CheckAndStage(cfg, UpdateLog));
                        nextUpdateCheck = cfg.Update.CheckMinutes > 0
                            ? DateTime.UtcNow.AddMinutes(cfg.Update.CheckMinutes)
                            : DateTime.MaxValue;
                    }

                    if (updateTask != null && updateTask.IsCompleted)
                    {
                        var finished = updateTask;
                        updateTask = null;
                        UpdateCheck check;
                        try
                        {
                            check = finished.GetAwaiter().GetResult();
                        }
                        catch (Exception e)
                        {
                            check = new UpdateCheck { Error = e.Message };
                        }

                        if (!check.Ok)
                        {
                            UpdateOnce("update: " + check.Error);
                        }
                        else if (!check.Newer)
                        {
                            UpdateOnce($"update: the game is up to date ({check.Installed})");
                        }
                        else if (check.Stage?.Ready != true)
                        {
                            UpdateOnce("update: " + (check.Error ?? "the newer release could not be prepared"));
                        }
                        else
                        {
                            string was = GameBuild.Version(cfg);
                            UpdateLog($"update: stopping {running.Count} server{(running.Count == 1 ? "" : "s")} for the file swap");
                            foreach (var server in running)
                                server.Stop();
                            running.Clear();

                            bool installed = Updater.Install(cfg, check.Stage, UpdateLog, out string problem);
                            if (!installed)
                                UpdateLog("update: " + problem + "; the servers start again on " +
                                          (was.Length > 0 ? was : "the build already there"));

                            // The engine's own server.ini and command line are unchanged by
                            // an update, but re-reading them is free and picks up a build
                            // whose layout differs.
                            prepared = PrepareAll(cfg, write: !dryRun);
                            gameStamp = GameBuild.Stamp(cfg);
                            updateNote = null;
                            LaunchAll();
                        }
                    }
                }

                if (cfg.RestartOnUpdate && !dryRun && ticks % 5 == 0)
                {
                    string now = GameBuild.Stamp(cfg);
                    if (now != gameStamp)
                    {
                        gameStamp = now;
                        string updated = GameBuild.Version(cfg);
                        Ui.Line($"{DateTime.Now:HH:mm:ss}  the game updated" +
                                (updated.Length > 0 ? $" to {updated}" : "") +
                                "; restarting every server on the new build");
                        foreach (var server in running)
                            server.Restart("the game updated");
                    }
                }

                foreach (var server in running)
                {
                    try
                    {
                        server.Poll();
                    }
                    catch (Exception e)
                    {
                        // One server's trouble must not take the others down.
                        Ui.Event(server.Name, "parked after an error: " + e.Message);
                        server.Park();
                    }
                }

                var lines = running.SelectMany(s => s.StatusLines()).ToList();
                int players = running.Sum(s => s.Players);
                var rcon = running.Select(s => s.RconWhere).Where(a => a != null).ToList();
                lines.Add($"{running.Count} server{(running.Count == 1 ? "" : "s")}   " +
                          $"{players} player{(players == 1 ? "" : "s")}   " +
                          (rcon.Count == 0 ? "RCON off" : $"RCON {string.Join(", ", rcon)}") +
                          "   Ctrl+C stops every server");
                Ui.Show(lines);
            }
        }
        finally
        {
            // Whatever ends this program, no engine is left running behind it.
            foreach (var server in running)
                server.Stop();
        }
        Ui.Line("all servers stopped.");
        Ui.End();
        return 0;
    }

    /// <summary>
    /// The RCON reload: read dedicated.toml again and put it into effect. A server
    /// whose settings changed is restarted on them, because its engine reads
    /// server.ini once at start; a server whose settings are the same is left
    /// alone, apart from RCON itself when that changed. The answer is what a panel
    /// shows, and "configuration not reloaded" means nothing was applied.
    /// </summary>
    static string ReloadAll(DedicatedConfig cfg, List<RunningServer> running, bool dryRun)
    {
        if (dryRun) return "configuration not reloaded: this host is only checking its setup";

        DedicatedConfig fresh;
        try
        {
            fresh = DedicatedConfig.Load(cfg.ConfigPath, verbose: false);
        }
        catch (ConfigError e)
        {
            return $"configuration not reloaded: {Path.GetFileName(e.File)} line {e.Line}: {e.Message}";
        }
        catch (Exception e)
        {
            return "configuration not reloaded: " + e.Message;
        }

        bool rconChanged = fresh.Rcon.Password != cfg.Rcon.Password || fresh.Rcon.Bind != cfg.Rcon.Bind;
        cfg.Rcon.Password = fresh.Rcon.Password;
        cfg.Rcon.Bind = fresh.Rcon.Bind;
        cfg.Defaults = fresh.Defaults;
        cfg.Warnings = fresh.Warnings;

        var again = new List<string>();
        var gone = new List<string>();
        foreach (var server in running)
        {
            var block = fresh.Servers.FirstOrDefault(s => s.Name == server.Name && s.Enabled)
                        ?? fresh.Servers.FirstOrDefault(s => s.Port == server.Config.Port && s.Enabled);
            if (block == null)
            {
                gone.Add(server.Name);
                continue;
            }
            string applied = server.ApplyConfig(fresh, block);
            if (applied == null) again.Add(server.Name);
            else if (applied.Length > 0) gone.Add(applied);
        }
        if (rconChanged)
        {
            foreach (var server in running) server.RestartRcon();
        }

        if (again.Count == 0 && gone.Count == 0 && !rconChanged)
            return "configuration reloaded: nothing changed";
        var parts = new List<string>();
        if (again.Count > 0) parts.Add("restarted " + string.Join(", ", again));
        if (rconChanged) parts.Add("RCON settings applied");
        if (gone.Count > 0) parts.Add("left as it was: " + string.Join(", ", gone));
        return "configuration reloaded: " + string.Join("; ", parts);
    }

    static string Version()
    {
        var v = typeof(Host).Assembly.GetName().Version;
        return v == null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}

/// <summary>One host-probe process and the files it keeps up to date.</summary>
sealed class RunningServer : IRconHost
{
    PreparedServer _prepared;
    readonly DedicatedConfig _cfg;
    readonly Func<string> _reload;       // re-reads dedicated.toml for the whole host
    Process _process;
    StreamWriter _log;
    DateTime _startedUtc;
    int _quickExits;
    bool _parked;
    bool _stopping;
    DateTime _nextStartUtc = DateTime.UtcNow;
    LiveStatus _last;
    string _lastWords;                   // the engine's own last line before it stopped
    RconListener _rcon;
    readonly Dictionary<string, PendingJoin> _pending = new();
    readonly Dictionary<string, int> _freshTried = new(StringComparer.OrdinalIgnoreCase); // fresh games ended per player
    readonly Dictionary<string, FreshGame> _freshGiven = new(StringComparer.OrdinalIgnoreCase); // a fresh game each player was given
    bool _upnpWarned;                    // the router's answer about forwarding is already on screen
    readonly HashSet<string> _gonePeers = new(StringComparer.OrdinalIgnoreCase);
    long _logRead;                       // how much of the engine's log this host has read
    long _freshGameFor;                  // the game we already ended for a late joiner
    DateTime _freshGameAt = DateTime.MinValue;
    DateTime _lastAnnounce = DateTime.MinValue;  // the last line this host said in this server's chat
    int _announceIndex;                            // which of the messages goes next

    /// <summary>
    /// A player who appeared in a game that was already running. They are the
    /// reason this host ends that game — but not before they have had time to
    /// finish loading the map, or their own client gives the join up.
    /// </summary>
    sealed class PendingJoin
    {
        public long GameStart;           // the game they joined
        public DateTime SeenAt;          // when this host first saw them
        public long SeenUnix;
        public bool Left;                // their client went away again before they spawned
    }

    /// <summary>
    /// A player this host already ended a game for. The engine drops them from the
    /// session while it reloads, then re-admits them to the game that replaced the
    /// one that was ended - which looks exactly like a second late join. It isn't:
    /// that is the fresh game they were given, and ending it would end every game
    /// after it too, so a player would never get to play.
    /// </summary>
    sealed class FreshGame
    {
        public long EndedGame;           // the game this host ended for them
        public DateTime At;              // when it was ended
    }

    public RunningServer(PreparedServer prepared, DedicatedConfig cfg, Func<string> reload = null)
    {
        _prepared = prepared;
        _cfg = cfg;
        _reload = reload;
    }

    public string Name => _prepared.Config.Name;

    public ServerConfig Config => _prepared.Config;

    public int Players => _last?.Players ?? 0;

    /// <summary>Where this server's RCON answers, or null when it is off.</summary>
    public string RconWhere => _rcon?.ToString();

    /// <summary>
    /// A changed dedicated.toml, put into effect: its native config is written
    /// again and the engine restarted on it, because server.ini is read once at
    /// start. Null means it was applied; otherwise the reason it couldn't be.
    /// </summary>
    public string ApplyConfig(DedicatedConfig fresh, ServerConfig block)
    {
        var again = Host.Prepare(fresh, block, write: true);
        if (!again.Ok)
            return $"{Name} ({string.Join("; ", again.Problems)})";

        bool same = SameSettings(_prepared, again, fresh);
        _prepared = again;
        if (same) return "";

        Ui.Event(Name, "restarting: its settings in dedicated.toml changed");
        StopProcess();
        StopRcon();
        Start();
        return null;
    }

    /// <summary>Whether a re-read config means the same engine arguments and files.</summary>
    static bool SameSettings(PreparedServer before, PreparedServer after, DedicatedConfig fresh)
    {
        if (before.IniPath != after.IniPath) return false;
        if (before.AdminPort != after.AdminPort) return false;
        if (before.Config.Port != after.Config.Port) return false;
        if (before.PlaylistName != after.PlaylistName) return false;
        string was = File.Exists(before.IniPath) ? File.ReadAllText(before.IniPath) : "";
        string now = Native.ServerIni(after.Config, fresh, after.Games, after.PlaylistName);
        return was == now && before.Games.Count == after.Games.Count;
    }

    /// <summary>RCON settings changed: the listener comes back on the new ones.</summary>
    public void RestartRcon()
    {
        StopRcon();
        StartRcon();
    }

    public bool Start()
    {
        _freshTried.Clear();   // a restarted engine gets a fresh try for everyone
        _pending.Clear();      // nobody is in a game of a session that hasn't started
        _freshGiven.Clear();
        var psi = _cfg.Launcher.StartInfo(_prepared.Dir, CommandLineArguments());
        string logPath = Path.Combine(_prepared.Dir, "server.log");

        Directory.CreateDirectory(_prepared.Dir);
        CloseLog();   // a restart opens the log again; the old handle must go first
        StreamWriter log;
        try
        {
            log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true,
            };
        }
        catch (Exception e)
        {
            Ui.Event(Name, "can't open its log: " + e.Message);
            return false;
        }
        _log = log;
        log.WriteLine($"== project-renaissance-dedicated started {Name} at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==");
        log.WriteLine("== " + Host.CommandLine(_prepared, _cfg));

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => EngineLine(log, e.Data);
        process.ErrorDataReceived += (_, e) => EngineLine(log, e.Data);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("the process didn't start");
        }
        catch (Exception e)
        {
            Ui.Event(Name, "can't start: " + e.Message + ExecutableHint(e));
            CloseLog();
            return false;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;
        _startedUtc = DateTime.UtcNow;
        _nextStartUtc = DateTime.UtcNow;

        Ui.Event(Name, $"started on UDP {_prepared.Config.Port}, " +
                       (_prepared.Config.PublicOn(_cfg.Defaults) ? $"listed on {_cfg.Master}" : "not listed") +
                       $", {_prepared.Games.Count} game(s)");
        Ui.Event(Name, $"dashboard http://127.0.0.1:{_prepared.AdminPort}/   log {logPath}");
        if (_cfg.Announce.On)
            Ui.Event(Name, $"announcement every {_cfg.Announce.EveryMinutes} min: {_cfg.Announce.Messages[0]}");
        StartRcon();
        return true;
    }

    /// <summary>
    /// RCON, so an owner can read a server and act on it while it runs. Off
    /// unless dedicated.toml sets [rcon] password; it listens on this PC by
    /// default, and on 0.0.0.0 only when that is asked for.
    /// </summary>
    void StartRcon()
    {
        if (_rcon != null) return;
        if (!_prepared.Config.RconOn(_cfg.Rcon)) return;

        var listener = RconListener.Start(this, _cfg.Rcon.Bind, _prepared.Config.RconPortOn(),
                                          _cfg.Rcon.Password, out string problem);
        if (listener == null)
        {
            Ui.Event(Name, "RCON is off: " + problem);
            return;
        }
        _rcon = listener;
        Ui.Event(Name, $"RCON on {_rcon} (any RCON panel connects there with the [rcon] password)");
    }

    void StopRcon()
    {
        _rcon?.Dispose();
        _rcon = null;
    }

    /// <summary>The engine's own last line in its log, which is usually the reason it stopped.</summary>
    string LastEngineLine()
    {
        try
        {
            string path = Path.Combine(_prepared.Dir, "server.log");
            if (!File.Exists(path)) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long from = Math.Max(0, stream.Length - 8192);
            stream.Seek(from, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string[] lines = reader.ReadToEnd().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("==", StringComparison.Ordinal)) continue;
                if (line.StartsWith("log opened", StringComparison.OrdinalIgnoreCase)) continue;
                return line.Length > 160 ? line.Substring(0, 157) + "..." : line;
            }
        }
        catch
        {
            // No log to quote is not a problem worth reporting.
        }
        return null;
    }

    /// <summary>A native engine that isn't executable says "permission denied": say what to do about it.</summary>
    string ExecutableHint(Exception e)
    {
        if (Platform.Windows || _cfg.Launcher.UnderWine) return "";
        string message = e.Message;
        for (Exception inner = e.InnerException; inner != null; inner = inner.InnerException)
            message += " / " + inner.Message;
        return message.Contains("denied", StringComparison.OrdinalIgnoreCase)
            ? $"; run: chmod +x \"{_cfg.Launcher.Program}\""
            : "";
    }

    /// <summary>The engine's output belongs in its log; --verbose also shows it here.</summary>
    void EngineLine(StreamWriter log, string line)
    {
        if (line == null) return;
        try
        {
            log.WriteLine(line);
        }
        catch
        {
            // A log that can't be written must not take the engine's reader down.
        }
        Ui.VerboseLine(Name, line);
    }

    List<string> CommandLineArguments() => Host.Arguments(_prepared, _cfg);

    public void Park()
    {
        _parked = true;
        Stop();
    }

    /// <summary>Stops the engine and starts it again at once, on the build now on disk.</summary>
    public void Restart(string why)
    {
        if (_parked || _stopping) return;
        Ui.Event(Name, "restarting: " + why);
        StopProcess();
        Start();
    }

    /// <summary>
    /// Keeps this server's status.json up to date, says what changed, ends a game
    /// a player joined late, and restarts the engine if it stops unexpectedly.
    /// </summary>
    public void Poll()
    {
        if (_parked) return;

        if (_process != null && _process.HasExited && !_stopping)
        {
            double uptime = (DateTime.UtcNow - _startedUtc).TotalSeconds;
            int exitCode = _process.ExitCode;
            Ui.Event(Name, $"stopped after {uptime:0}s (exit {exitCode}); see {Path.Combine(_prepared.Dir, "server.log")}");
            _log?.WriteLine($"== stopped after {uptime:0}s, exit code {exitCode}");
            CloseLog();

            _last = null;                 // the block must not keep showing a stopped engine's game
            // Exit 0 is the engine doing what it was told: its own recycle after
            // recycle_after_games games, or a stop this host asked for. Counting that
            // as a crash is what parked a healthy server at 00:42 on 2026-10-29.
            bool planned = exitCode == 0;
            _quickExits = planned ? 0 : uptime < 20 ? _quickExits + 1 : 0;
            if (_quickExits >= 10)
            {
                _parked = true;
                // Its own last line usually says why: a master that isn't answering,
                // a map that won't load, MCC that changed.
                _lastWords ??= LastEngineLine();
                Ui.Event(Name, $"parked: it stopped {_quickExits} times in a row without getting going" +
                               (_lastWords != null ? $" — its log ends with: {_lastWords}" : "") +
                               ". Fix the reason, then start this host again.");
                return;
            }
            if (_quickExits >= 1)
                _lastWords ??= LastEngineLine();
            // A planned recycle wants its place back in a moment; a failure backs off
            // further each time instead of hammering the port ten times a minute.
            int wait = planned ? 3 : Math.Min(120, 5 * (1 << Math.Min(_quickExits - 1, 5)));
            _nextStartUtc = DateTime.UtcNow.AddSeconds(wait);
            if (_quickExits >= 2)
                Ui.Event(Name, $"trying again in {wait}s" +
                               (_lastWords != null ? $" (its log ends with: {_lastWords})" : ""));
            _process = null;
        }

        if (_process == null && !_stopping && DateTime.UtcNow >= _nextStartUtc)
            Start();

        // A status.json from an engine that has stopped is not this server's state:
        // it would show the last game's players as if they were still here.
        ScanEngineLog();
        CheckUpnp();

        var status = LiveStatus.Read(_prepared.Dir);
        if (status == null || !status.Fresh) return;
        var before = _last;

        if (before == null)
        {
            if (status.State != null)
                Ui.Event(Name, $"{status.State}: {status.Map} / {status.Mode}  ({status.Players}/{status.MaxPlayers} players)");
        }
        else
        {
            var joined = NewNames(before.PlayerNames, status.PlayerNames);
            var left = NewNames(status.PlayerNames, before.PlayerNames);

            if (joined.Count > 0)
                Ui.Event(Name, $"{string.Join(", ", joined)} joined ({status.Players}/{status.MaxPlayers} playing)");
            if (left.Count > 0)
                Ui.Event(Name, $"{string.Join(", ", left)} left ({status.Players}/{status.MaxPlayers} playing)");
            if (status.Map != before.Map || status.Mode != before.Mode)
                Ui.Event(Name, $"now playing {status.Map} / {status.Mode}");
            if (status.State != before.State)
                Ui.Event(Name, "state: " + status.State);

            TrackJoins(status, joined);
            FreshGameForLateJoiner(status);
        }


        bool changed = before == null || status.State != before.State || status.Map != before.Map ||
                       status.Mode != before.Mode || status.Players != before.Players ||
                       !status.PlayerNames.SequenceEqual(before.PlayerNames);
        status.RconAddress = RconWhere;
        if (changed) status.WriteOwn(_prepared.Dir, _prepared.Config.Port);
        _last = status;
        AnnounceTick(status);
    }

    /// <summary>
    /// The [announce] table's bot: every so many minutes, one line from the server
    /// into the players' own chat. It is the engine's match chat underneath — the
    /// same path an owner's `say` takes — and the engine only relays it while a
    /// match with an online session is up, so nothing is said into an empty server.
    /// A server that has just gone from empty to occupied says its line at once,
    /// which is when a welcome is worth reading.
    /// </summary>
    void AnnounceTick(LiveStatus status)
    {
        var settings = _cfg.Announce;
        if (!settings.On || _parked) return;
        if (status.Players <= 0) return;        // nobody is there to read it
        if ((DateTime.UtcNow - _lastAnnounce).TotalMinutes < settings.EveryMinutes) return;
        _lastAnnounce = DateTime.UtcNow;
        string text = settings.Messages[_announceIndex % settings.Messages.Count];
        _announceIndex++;
        var result = Chat.Announce(_prepared.Dir, text);
        Ui.Event(Name, "announcement: " + text);
        _log?.WriteLine($"== announcement: {text}");
        if (!result.Session) _log?.WriteLine("== that one wasn't relayed: " + result.Note);
    }

    /// <summary>Remembers who joined which game, and forgets the ones who left.</summary>
    void TrackJoins(LiveStatus status, List<string> joined)
    {
        foreach (string name in _pending.Keys.ToList())
        {
            if (!status.PlayerNames.Contains(name)) _pending[name].Left = true;
            // A connection the engine has seen close is gone now, even though the
            // session still counts that player until it times their route out.
            var player = status.Roster.FirstOrDefault(p => p.Shown == name);
            if (player?.Peer != null && _gonePeers.Contains(player.Peer)) _pending[name].Left = true;
            // A new game clears the slate: whoever is in it joined it at its start.
            if (_pending[name].GameStart != status.StartedUnix)
                _pending.Remove(name);
        }
        // A player this host ended a game for is carried into the game that replaced
        // it. Their arrival there is the fresh start they were given, not another
        // late join, so it must not end that game in turn.
        foreach (string name in _freshGiven.Keys.ToList())
            if ((DateTime.UtcNow - _freshGiven[name].At).TotalMinutes > 10)
                _freshGiven.Remove(name);      // they never made it in: forget the favour

        foreach (string name in joined)
        {
            if (_freshGiven.TryGetValue(name, out var given) && status.StartedUnix > given.EndedGame)
            {
                _pending.Remove(name);
                _freshGiven.Remove(name);      // they are in the fresh game now; a later join is a real one
                _freshTried.Remove(name);
                continue;
            }
            _pending[name] = new PendingJoin
            {
                GameStart = status.StartedUnix,
                SeenAt = DateTime.UtcNow,
                SeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
        }
    }

    /// <summary>
    /// The engine's log says when a connection closed or a route was dropped, which
    /// is how this host knows a joiner's client faulted on the way in — their client
    /// is gone long before the session stops counting them as a player.
    /// </summary>
    void ScanEngineLog()
    {
        try
        {
            string path = Path.Combine(_prepared.Dir, "server.log");
            if (!File.Exists(path)) return;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _logRead) _logRead = 0;      // a new folder's log starts over
            if (stream.Length == _logRead) return;
            stream.Seek(_logRead, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            _logRead = stream.Length;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.Contains("connect-closed") && !line.Contains("route dropped")) continue;
                int at = line.IndexOf("0x", StringComparison.Ordinal);
                if (at < 0) continue;
                var peer = new System.Text.StringBuilder();
                for (int i = at + 2; i < line.Length && Uri.IsHexDigit(line[i]) && peer.Length < 16; i++)
                    peer.Append(char.ToLowerInvariant(line[i]));
                if (peer.Length == 16) _gonePeers.Add(peer.ToString());
            }
        }
        catch
        {
            // Its log is a convenience here, never a reason to stop a server.
        }
    }

    /// <summary>
    /// Reach Renaissance can't spawn a player into a game already in progress: the
    /// engine admits them, hands their client the map and the game type, and never
    /// puts them in the world — the screen stays black and the scoreboard reads DEAD
    /// for as long as that game lasts.
    ///
    /// Ending that game while their client is loading it does not rescue the join on
    /// this engine build: the client drops the join (`join: this PC's game closed its
    /// connection to the host before the map loaded`) or loads the next map and
    /// faults. So this is off by default: a join is left alone, and a server that
    /// waits for its first player (`wait_for_players`) starts them in the game.
    ///
    /// A player gets a few of these (three, then this host stops ending other
    /// people's games for them), so a client the engine will never spawn can't keep
    /// the rotation stuck. A game that players are actually playing is only ended
    /// for someone who is still connected to it.
    /// </summary>

    void FreshGameForLateJoiner(LiveStatus status)
    {
        if (_pending.Count == 0) return;
        if (!_prepared.Config.FreshOnJoin(_cfg.Defaults)) return;
        if (status.State != "playing" || status.StartedUnix <= 0) return;
        if (_freshGameFor == status.StartedUnix) return;    // this game was already ended for one joiner
        if ((DateTime.UtcNow - _freshGameAt).TotalSeconds < 30) return;

        int delay = _prepared.Config.FreshJoinDelayOn(_cfg.Defaults);
        var ready = new List<string>();
        bool genuine = status.Roster.Any(p => !Stuck(p.Shown, status.StartedUnix));
        foreach (var (who, join) in _pending)
        {
            if (join.GameStart != status.StartedUnix) continue;
            if (_freshTried.GetValueOrDefault(who) >= 3) continue;   // a few tries, never a loop
            // A joiner whose client is already gone has no map load left to protect:
            // end it now, so the fresh game is up by the time they click join again.
            double waited = (DateTime.UtcNow - join.SeenAt).TotalSeconds;
            if (!join.Left && waited < delay) continue;
            if (join.Left && waited < 8) continue;
            // A joiner who has gone away again (their client faulted on the way in)
            // only gets the game ended while nobody else is playing it, because
            // players who were here before this game started have earned their game.
            if (!join.Left || !genuine) ready.Add(who);
        }
        if (ready.Count == 0) return;

        var reply = AdminApi.Send(_prepared.AdminPort, AdminApi.Password(_prepared.Dir),
                                  new Dictionary<string, object> { ["action"] = "end_game" });
        _freshGameFor = status.StartedUnix;
        _freshGameAt = DateTime.UtcNow;
        foreach (string name in ready)
        {
            _freshTried[name] = _freshTried.GetValueOrDefault(name) + 1;
            // The next game is theirs: its start is what tells this host that their
            // arrival there is the fresh start, not another game to end.
            _freshGiven[name] = new FreshGame { EndedGame = status.StartedUnix, At = DateTime.UtcNow };
        }
        bool left = ready.Any(name => _pending[name].Left);
        string names = string.Join(", ", ready);
        if (!reply.Ok)
            Ui.Event(Name, $"couldn't end the game for {names}: {reply.Message}");
        else if (left)
            Ui.Event(Name, $"{names} left while the game was loading their map; moved the rotation on so their next join lands in a new game");
        else
            Ui.Event(Name, $"{names} joined a game already in progress, which Reach Renaissance can't spawn them into; ending it so they start in the next one");
    }

    // ---- RCON: what an owner (or their panel) can ask this server -------------

    /// <summary>
    /// UPnP is how the engine asks the router to let players in. When the router
    /// answers "no gateway", players on the internet can't open a direct connection:
    /// the server list still gets them in, through its own relay, and a relayed game
    /// is a laggy game. That line only ever went to upnp.log, so say it here, once,
    /// with the port to forward.
    /// </summary>
    void CheckUpnp()
    {
        if (_upnpWarned) return;
        if (!(_prepared.Config.Upnp ?? _cfg.Upnp)) return;
        string path = Path.Combine(_prepared.Dir, "upnp.log");
        try
        {
            if (!File.Exists(path)) return;
            if (!File.ReadAllText(path).Contains("no Internet gateway answered")) return;
        }
        catch
        {
            return;
        }
        _upnpWarned = true;
        int port = _prepared.Config.Port;
        Ui.Event(Name, $"the router wouldn't open UDP and TCP {port} for this server (UPnP is off on it), " +
                       $"so players off your network can only reach it through the server list's relay, " +
                       $"which plays laggy. Forward UDP {port} and TCP {port} on the router to this PC for a " +
                       "direct connection, then restart the server.");
    }

    /// <summary>
    /// The engine's own view, read fresh so an RCON answer is never a second old.
    /// Falls back to the host's last poll when the dashboard is between writes.
    /// </summary>
    LiveStatus Live()
    {
        var status = LiveStatus.Read(_prepared.Dir);
        return status?.Fresh == true ? status : _last;
    }

    public string RconStatus()
    {
        var s = Live();
        if (s == null) return $"{Name} isn't running yet ({_prepared.Dir})";
        var lines = new List<string>
        {
            $"{Name}: {s.State ?? "unknown"}",
            s.Map != null
                ? $"Current game: {s.Mode} on {s.Map}" + (s.StartedUnix > 0
                    ? $" (started {Ago(s.StartedUnix)})" : "")
                : "No game is running.",
            $"Players: {s.Players}/{s.MaxPlayers}",
        };
        if (s.NextMap != null) lines.Add($"Next: {s.NextMode} on {s.NextMap}");
        if (s.PlaylistTitle != null) lines.Add($"Rotation: {s.PlaylistTitle} ({s.Source})");
        lines.Add($"Engine: build {s.Build}, pid {s.Pid}, udp {s.Port}, up {Ago(_startedUtc)}");
        if (RconWhere != null) lines.Add($"RCON: {RconWhere} (this)");
        return string.Join('\n', lines.Where(l => l.Length > 0));
    }

    public string RconPlayers()
    {
        var s = Live();
        if (s == null) return "this server isn't running yet";
        if (s.Roster.Count == 0) return "no players\n0/" + s.MaxPlayers + " players";

        var lines = new List<string>();
        int index = 0;
        foreach (var player in s.Roster)
        {
            index++;
            var facts = new List<string>();
            if (player.Id64 != null) facts.Add("id: " + player.Id64);
            if (player.Peer != null) facts.Add("peer: " + player.Peer);
            if (player.Ip != null) facts.Add("address: " + player.Ip);
            if (player.Relayed) facts.Add("relayed");
            if (player.Banned) facts.Add("banned, being removed");
            long game = s.StartedUnix;
            if (GameStartOf(player) is long seen && seen > game && game > 0)
                facts.Add("joined after this game started");
            lines.Add($"{index}. name: {player.Shown}, " + string.Join(", ", facts));
        }
        lines.Add($"{s.Players}/{s.MaxPlayers} players");
        return string.Join('\n', lines);
    }

    // When this host first saw a player, for "joined after this game started".
    long? GameStartOf(LivePlayer player)
    {
        return _pending.TryGetValue(player.Shown, out var join) ? join.GameStart : null;
    }

    /// <summary>Did this host watch them arrive after the game they are in had started?</summary>
    bool Stuck(string name, long gameStart) =>
        _pending.TryGetValue(name, out var join) && join.GameStart == gameStart;

    public string RconSay(string text)
    {
        var result = Chat.Announce(_prepared.Dir, text);
        Ui.Event(Name, "announcement: " + text.Trim());
        _log?.WriteLine($"== announcement: {text.Trim()}");
        return result.Note;
    }

    public string RconKick(string target, string reason)
    {
        var player = FindPlayer(target, out string problem);
        if (player == null) return problem;
        if (player.Peer == null) return $"{player.Shown} hasn't got a network id yet; give it a moment";
        var command = new Dictionary<string, object> { ["action"] = "kick", ["peer"] = player.Peer };
        if (!string.IsNullOrWhiteSpace(reason)) command["reason"] = reason.Trim();
        var reply = AdminApi.Send(_prepared.AdminPort, AdminApi.Password(_prepared.Dir), command);
        if (!reply.Ok) return "the engine wouldn't kick them: " + reply.Message;
        Ui.Event(Name, $"kicked {player.Shown}" + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}"));
        _log?.WriteLine($"== kicked {player.Shown} ({player.Peer}) {reason}".TrimEnd());
        return $"kicked {player.Shown}. They can come back: use ban to keep them out.";
    }

    public string RconBan(string target, string duration, string reason)
    {
        var command = new Dictionary<string, object> { ["action"] = "ban" };
        string who;
        if (LooksLikeId(target, out string id))
        {
            command["engine_id"] = id;
            who = TargetName(target) ?? id;
        }
        else if (LooksLikeAddress(target, out string address, out int? range))
        {
            command["ip"] = address;
            if (range != null) command["range"] = range.Value;
            who = target;
        }
        else
        {
            return $"\"{target}\" isn't a player id (16 hex digits, or the 64 a panel shows) or an address, such as 203.0.113.7 or 203.0.113.0/24";
        }
        if (!string.IsNullOrEmpty(duration))
        {
            int? minutes = RconCommands.Minutes(duration);
            if (minutes == null) return $"\"{duration}\" isn't a ban length; use 30m, 12h, 7d or 2w (or leave it out for permanent)";
            command["minutes"] = minutes.Value;
        }
        command["reason"] = string.IsNullOrWhiteSpace(reason) ? "Banned from the server" : reason.Trim();

        var reply = AdminApi.Send(_prepared.AdminPort, AdminApi.Password(_prepared.Dir), command);
        if (!reply.Ok) return "the engine wouldn't ban them: " + reply.Message;
        Ui.Event(Name, $"banned {who}" + (string.IsNullOrWhiteSpace(reason) ? "" : $": {reason.Trim()}") +
                       (string.IsNullOrEmpty(duration) ? " (permanent)" : $" for {duration}"));
        _log?.WriteLine($"== banned {who} {duration} {reason}".TrimEnd());
        return $"banned {who}" + (string.IsNullOrEmpty(duration) ? " (permanent)" : $" for {duration}");
    }

    public string RconUnban(string target)
    {
        var bans = ReadBans(out string problem);
        if (bans == null) return problem;
        foreach (var ban in bans)
        {
            if (MatchesBan(ban, target)) 
            {
                var id = ban.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String ? idv.GetString() : null;
                if (id == null) continue;
                var reply = AdminApi.Send(_prepared.AdminPort, AdminApi.Password(_prepared.Dir),
                                          new Dictionary<string, object> { ["action"] = "unban", ["id"] = id });
                if (!reply.Ok) return "the engine wouldn't lift the ban: " + reply.Message;
                string name = ban.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                Ui.Event(Name, $"unbanned {name ?? target}");
                _log?.WriteLine($"== unbanned {name ?? target}");
                return $"unbanned {name ?? target}";
            }
        }
        return $"nobody here is banned as \"{target}\" (try: bans)";
    }

    public string RconBans()
    {
        var bans = ReadBans(out string problem);
        if (bans == null) return problem;
        if (bans.Count == 0) return "nobody is banned";
        var lines = new List<string>();
        int index = 0;
        foreach (var ban in bans)
        {
            index++;
            string name = Text(ban, "name") ?? "no name";
            string id = Text(ban, "engine_id");
            string ip = Text(ban, "ip");
            long until = ban.TryGetProperty("until_unix", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : 0;
            string reason = Text(ban, "reason");
            var facts = new List<string>();
            if (id != null) facts.Add("id " + id.PadLeft(64, '0'));
            if (ip != null) facts.Add("address " + ip);
            facts.Add(until > 0 ? "until " + DateTimeOffset.FromUnixTimeSeconds(until).ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "permanent");
            if (reason != null) facts.Add($"\"{reason}\"");
            lines.Add($"{index}. {name}: " + string.Join(", ", facts));
        }
        lines.Add($"{bans.Count} ban(s) in {Path.GetFileName(_cfg.Bans)}, shared by every server");
        return string.Join('\n', lines);
    }

    public string RconEndGame() => EngineAction(new Dictionary<string, object> { ["action"] = "end_game" },
                                               "ended this game; the engine starts the next one");

    public string RconEndRound() => EngineAction(new Dictionary<string, object> { ["action"] = "end_round" },
                                                 "ended this round");

    /// <summary>The scenarios this server can play, one per line for a panel's map list.</summary>
    public string RconMaps()
    {
        var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in _prepared.Games)
            if (!string.IsNullOrWhiteSpace(game.Map)) maps.Add(game.Map);
        if (!string.IsNullOrWhiteSpace(_last?.Map)) maps.Add(_last.Map);
        return maps.Count == 0
            ? "no maps in this server's rotation"
            : string.Join("\n", maps.OrderBy(m => m, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The game types this host can see, one file name per line for a panel.</summary>
    public string RconModes()
    {
        var modes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in _prepared.Games)
            if (!string.IsNullOrWhiteSpace(game.Game))
                modes.Add(Path.GetFileName(game.Game.Replace('\\', '/')));
        var contents = new NativeContents(_cfg);
        foreach (string file in contents.ListGameTypes())
            modes.Add(Path.GetFileName(file));
        return modes.Count == 0
            ? "no game types found; put .bin files in Game Modes or set mcc"
            : string.Join("\n", modes.OrderBy(m => m, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A panel's map/load commands: map and game are resolved with the same rules a
    /// playlist uses, then handed to the engine as the map:game pair it already reads.
    /// </summary>
    public string RconLoad(string map, string mode, bool now)
    {
        if (string.IsNullOrWhiteSpace(map))
            return now ? "usage: load <map> [game type]" : "usage: nextmap <map> [game type]";

        var problems = new List<string>();
        var contents = new NativeContents(_cfg);
        string resolvedMap = contents.Resolve(map, problems, isMap: true);
        string resolvedGame = string.IsNullOrWhiteSpace(mode)
            ? _last?.Mode
            : contents.Resolve(mode, problems, isMap: false);
        if (resolvedMap == null || string.IsNullOrWhiteSpace(resolvedGame))
            return problems.Count > 0
                ? string.Join("; ", problems)
                : "choose a game type too; this server has no current game to keep";

        return RconNext((now ? "now " : "") + resolvedMap + ":" + resolvedGame);
    }

    public string RconNext(string game)
    {
        bool now = false;
        string text = game.Trim();
        if (text.StartsWith("now ", StringComparison.OrdinalIgnoreCase))
        {
            now = true;
            text = text.Substring(4).Trim();
        }
        var command = new Dictionary<string, object> { ["action"] = "next", ["game"] = text };
        if (now) command["now"] = true;
        return EngineAction(command, now ? $"{text} plays next (this game ends now)" : $"{text} plays next");
    }

    public string RconRestart(string when)
    {
        string value = string.IsNullOrWhiteSpace(when) ? "now" : when.Trim().ToLowerInvariant();
        if (value == "now") return EngineAction(new Dictionary<string, object> { ["action"] = "restart" },
                                                "restarting this server now");
        if (value is not ("empty" or "game_end" or "game-end"))
            return "restart takes now, empty, or game_end";
        return EngineAction(new Dictionary<string, object> { ["action"] = "restart", ["when"] = value.Replace('-', '_') },
                            value.StartsWith("empty") ? "this server restarts once nobody is playing"
                                                      : "this server restarts after this game");
    }

    public string RconReload() => _reload != null ? _reload() : "this host can't reload its configuration";

    string EngineAction(Dictionary<string, object> command, string done)
    {
        var reply = AdminApi.Send(_prepared.AdminPort, AdminApi.Password(_prepared.Dir), command);
        if (!reply.Ok) return "the engine wouldn't do that: " + reply.Message;
        Ui.Event(Name, done);
        _log?.WriteLine("== " + done);
        return done + ".";
    }

    /// <summary>A player by name, by the id a panel shows, or by address.</summary>
    LivePlayer FindPlayer(string target, out string problem)
    {
        problem = null;
        var s = Live();
        if (s == null)
        {
            problem = "this server isn't running yet";
            return null;
        }
        if (LooksLikeId(target, out string id))
        {
            var byId = s.Roster.FirstOrDefault(p => p.EngineId != null &&
                                                    string.Equals(NormalizeId(p.EngineId), id, StringComparison.OrdinalIgnoreCase));
            if (byId != null) return byId;
            problem = $"nobody in this game has the id {id}";
            return null;
        }
        var byName = s.Roster.Where(p => string.Equals(p.Shown, target, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count == 1) return byName[0];
        if (byName.Count > 1)
        {
            problem = $"two players here are called {target}; give the id from players";
            return null;
        }
        var byIp = s.Roster.Where(p => p.Ip != null && p.Ip.StartsWith(target, StringComparison.Ordinal)).ToList();
        if (byIp.Count == 1) return byIp[0];
        problem = $"nobody in this game matches \"{target}\"; players lists who is here";
        return null;
    }

    string TargetName(string target) =>
        FindPlayer(target, out _)?.Shown;

    /// <summary>
    /// One player id, however it was written: the 16 hex digits the engine keeps,
    /// the 64 a panel prints (the same id, zero-extended), or a short form a person
    /// copied by eye. All of those name the same player.
    /// </summary>
    static bool LooksLikeId(string target, out string id)
    {
        id = null;
        string text = (target ?? "").Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text.Substring(2);
        if (text.Length == 0 || text.Length > 64 || !text.All(Uri.IsHexDigit)) return false;
        if (text.Length > 16)
        {
            // A panel's 64 digits are the engine's 16 with zeros in front of them.
            string head = text.Substring(0, text.Length - 16);
            if (head.Any(c => c != '0')) return false;
            text = text.Substring(text.Length - 16);
        }
        id = NormalizeId(text);
        return true;
    }

    /// <summary>An id as the engine keeps it: 16 hex digits, lower case.</summary>
    public static string NormalizeId(string id) =>
        (id ?? "").Trim().TrimStart('0').ToLowerInvariant().PadLeft(16, '0');

    static bool LooksLikeAddress(string target, out string address, out int? range)
    {
        address = null;
        range = null;
        string text = (target ?? "").Trim();
        string head = text;
        int slash = text.IndexOf('/');
        if (slash > 0)
        {
            head = text.Substring(0, slash);
            if (!int.TryParse(text.Substring(slash + 1), out int bits)) return false;
            range = bits;
        }
        if (head.Length == 0 || head.Contains(':')) return false;   // v6 addresses: the engine takes v4
        var parts = head.Split('.');
        if (parts.Length != 4) return false;
        foreach (string part in parts)
            if (!byte.TryParse(part, out _)) return false;
        address = head;
        return true;
    }

    List<JsonElement> ReadBans(out string problem)
    {
        problem = null;
        var (ok, text, why) = AdminApi.Get(_prepared.AdminPort, AdminApi.Password(_prepared.Dir), "/api/bans");
        if (!ok)
        {
            problem = "can't read the ban list: " + why;
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("bans", out var bans) || bans.ValueKind != JsonValueKind.Array)
            {
                problem = "the engine's ban list didn't make sense";
                return null;
            }
            // The document dies with this method, so each ban is cloned out of it.
            return bans.EnumerateArray().Select(b => b.Clone()).ToList();
        }
        catch (Exception e)
        {
            problem = "the engine's ban list didn't make sense: " + e.Message;
            return null;
        }
    }

    /// <summary>Does this ban cover what the caller named: an id, or an address or range?</summary>
    static bool MatchesBan(JsonElement ban, string target)
    {
        if (LooksLikeId(target, out string id) && Text(ban, "engine_id") is string banned &&
            string.Equals(NormalizeId(banned), id, StringComparison.OrdinalIgnoreCase))
            return true;
        string ip = Text(ban, "ip");
        if (ip == null) return false;
        string text = target.Trim();
        if (string.Equals(ip, text, StringComparison.Ordinal)) return true;
        // A panel sends 203.0.113.0/24; the engine keeps the address and its width apart.
        if (ban.TryGetProperty("range", out var r) && r.ValueKind == JsonValueKind.Number)
            return text.StartsWith(ip + "/", StringComparison.Ordinal);
        return false;
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string Ago(long unix)
    {
        long seconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix;
        if (seconds < 0) seconds = 0;
        if (seconds < 60) return $"{seconds}s ago";
        if (seconds < 3600) return $"{seconds / 60}m ago";
        return $"{seconds / 3600}h{seconds % 3600 / 60:00}m ago";
    }

    static string Ago(DateTime when)
    {
        var span = DateTime.UtcNow - when;
        if (span.TotalMinutes < 1) return $"{(int)span.TotalSeconds}s";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m";
        return $"{(int)span.TotalHours}h{span.Minutes:00}m";
    }

    /// <summary>The names in after that before didn't have, counting duplicates.</summary>
    static List<string> NewNames(List<string> before, List<string> after)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string name in before) counts[name] = counts.TryGetValue(name, out int n) ? n + 1 : 1;
        var added = new List<string>();
        foreach (string name in after)
        {
            if (counts.TryGetValue(name, out int n) && n > 0) counts[name] = n - 1;
            else added.Add(name);
        }
        return added;
    }

    /// <summary>
    /// This server's lines in the live block: what it is doing, then one line per
    /// player in it. The block is redrawn in place, so a server appears once and
    /// who is playing shows up under it as they join and leave.
    /// </summary>
    public List<string> StatusLines()
    {
        var lines = new List<string>();
        string name = Name.Length > 26 ? Name.Substring(0, 25) + "…" : Name.PadRight(26);
        if (_parked)
        {
            lines.Add($"{name} parked: it kept failing to start; see its log");
            return lines;
        }
        if (_last == null)
        {
            lines.Add($"{name} starting...");
            return lines;
        }

        string listed = _prepared.Config.PublicOn(_cfg.Defaults) ? "listed" : "not listed";
        lines.Add($"{name} {_last.State,-9} {_last.Players,2}/{_last.MaxPlayers}  {_last.Map} / {_last.Mode}" +
                  $"   {listed}, up {Uptime()}");
        foreach (var player in _last.Roster)
        {
            var facts = new List<string>();
            if (player.Id64 != null) facts.Add(player.EngineId.ToLowerInvariant());
            if (player.Ip != null) facts.Add(player.Ip);
            if (player.Relayed) facts.Add("relayed");
            if (player.Banned) facts.Add("banned, being removed");
            if (_pending.TryGetValue(player.Shown, out var join) && join.GameStart == _last.StartedUnix)
            {
                double waited = (DateTime.UtcNow - join.SeenAt).TotalSeconds;
                if (join.Left) facts.Add("left while joining");
                else if (waited < 5) facts.Add("joining");
                else facts.Add($"loading/just joined {(int)waited}s");
            }
            lines.Add($"    {player.Shown,-20} " + string.Join("   ", facts));
        }
        return lines;
    }

    string Uptime()
    {
        if (_process == null) return "stopped";
        var span = DateTime.UtcNow - _startedUtc;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}h{span.Minutes:00}m" : $"{(int)span.TotalMinutes}m";
    }

    public void Stop()
    {
        _stopping = true;
        StopProcess();
        StopRcon();
    }

    /// <summary>
    /// Stops the engine and lets go of everything holding its port. RCON listens on
    /// 127.0.0.1:port, and the engine's own server-list listener wants
    /// 0.0.0.0:port: while this host still holds the first, the engine's next start
    /// dies with "cannot listen on TCP 0.0.0.0:port ... (os error 10048)". So RCON
    /// is closed here, and Start() opens it again once the engine is up.
    /// </summary>
    void StopProcess()
    {
        StopRcon();
        var process = _process;
        _process = null;
        if (process != null && !process.HasExited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception e)
            {
                Ui.Event(Name, "couldn't stop cleanly: " + e.Message);
            }
        }
        CloseLog();
    }

    void CloseLog()
    {
        try
        {
            _log?.Flush();
            _log?.Dispose();
        }
        catch
        {
            // Nothing useful to do about a log that won't close.
        }
        _log = null;
    }
}
