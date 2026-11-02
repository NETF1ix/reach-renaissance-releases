using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>One player the engine has in its session right now.</summary>
sealed class LivePlayer
{
    public string Peer;        // 16 hex digits: the engine's own network peer id
    public string EngineId;    // 16 hex digits: the player's Reach Renaissance id
    public string Name;
    public string Ip;
    public bool Banned;
    public bool Relayed;

    public string Shown => string.IsNullOrWhiteSpace(Name) ? "player" : Name;

    /// <summary>
    /// The player id a panel can use: the mod's 16 hex digits, zero-extended to the
    /// 64 hex digits a Source RCON panel looks for. This host takes either form back.
    /// </summary>
    public string Id64 => EngineId == null ? null : EngineId.ToLowerInvariant().PadLeft(64, '0');
}

/// <summary>
/// What one running server reports: read from the status.json its dashboard
/// keeps in the server folder's admin/ folder.
/// </summary>
sealed class LiveStatus
{
    public string Name;
    public int Port;
    public int AdminPort;
    public int Pid;
    public string Build;
    public string State;
    public string Map;
    public string Mode;
    public int Players;
    public List<string> PlayerNames = new();
    public List<LivePlayer> Roster = new();
    public string NextMap;            // what plays after this game, when the engine says
    public string NextMode;
    public bool VoteOn;               // the map vote's own settings, for status and reload checks
    public int VoteTime;
    public int MaxPlayers;
    public string Source;
    public string PlaylistTitle;
    public long StartedUnix;          // when the game now playing started; 0 between games
    public long Unix;

    public static LiveStatus Read(string serverDir)
    {
        string file = Path.Combine(serverDir, "admin", "status.json");
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var s = new LiveStatus();
            if (root.TryGetProperty("server", out var server))
            {
                s.Name = Text(server, "name");
                s.Port = Number(server, "port");
                s.AdminPort = Number(server, "admin_port");
                s.Pid = Number(server, "pid");
                s.Build = Text(server, "build");
            }
            s.State = Text(root, "state");
            s.MaxPlayers = Number(root, "max_players");
            s.Unix = (long)Number64(root, "unix");
            if (root.TryGetProperty("game", out var game))
            {
                s.Map = Text(game, "map");
                s.Mode = Text(game, "shown") ?? Path.GetFileNameWithoutExtension(Text(game, "mode") ?? "");
                s.StartedUnix = Number64(game, "started_unix");
            }
            if (root.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
            {
                foreach (var player in players.EnumerateArray())
                {
                    if (player.ValueKind != JsonValueKind.Object) continue;
                    var who = new LivePlayer
                    {
                        Peer = Text(player, "peer"),
                        EngineId = Text(player, "engine_id"),
                        Name = Text(player, "name"),
                        Ip = Text(player, "ip"),
                        Banned = Bool(player, "banned"),
                        Relayed = Bool(player, "relayed"),
                    };
                    s.Roster.Add(who);
                    s.Players++;
                    // The engine lists a player the moment they connect, before their
                    // name arrives from the menus; say "player" rather than nothing.
                    s.PlayerNames.Add(who.Shown);
                }
            }
            if (root.TryGetProperty("rotation", out var next) && next.TryGetProperty("next", out var queued) &&
                queued.ValueKind == JsonValueKind.Object && queued.TryGetProperty("game", out var nextGame) &&
                nextGame.ValueKind == JsonValueKind.String)
            {
                string[] parts = nextGame.GetString().Split(':');
                if (parts.Length > 1)
                {
                    s.NextMap = parts[0];
                    s.NextMode = Path.GetFileNameWithoutExtension(parts[1]);
                }
            }
            if (root.TryGetProperty("rotation", out var rotation))
            {
                s.Source = Text(rotation, "source");
                if (rotation.TryGetProperty("playlist", out var playlist) && playlist.ValueKind == JsonValueKind.Object)
                    s.PlaylistTitle = Text(playlist, "title") ?? Text(playlist, "name");
                if (rotation.TryGetProperty("vote", out var vote) && vote.ValueKind == JsonValueKind.Object)
                {
                    s.VoteOn = Bool(vote, "on");
                    s.VoteTime = Number(vote, "time");
                }
            }
            return s;
        }
        catch
        {
            return null;   // mid-write or unreadable: the next poll picks it up
        }
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static int Number(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    static long Number64(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;

    static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public bool Fresh => DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Unix < 30;

    /// <summary>
    /// The same status.json Project Reclaimer's servers keep, so tools that read
    /// that file find this one too.
    /// </summary>
    public void WriteOwn(string serverDir, int port)
    {
        string address = ServerAddress() + ":" + port;
        var json = new Dictionary<string, object>
        {
            ["name"] = Name ?? Path.GetFileName(serverDir),
            ["address"] = address,
            ["map"] = Map ?? "",
            ["mode"] = Mode ?? "",
            ["players"] = PlayerNames.ToArray(),
            ["rcon"] = RconText(),
            ["updated"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        try
        {
            Directory.CreateDirectory(serverDir);
            File.WriteAllText(Path.Combine(serverDir, "status.json"),
                JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }
        catch
        {
            // A status snapshot is not worth failing a server over.
        }
    }

    /// <summary>Where this server's RCON answers, for tools that read status.json.</summary>
    public string RconAddress;

    string RconText() => RconAddress ?? "off";

    public static string ServerAddress()
    {
        try
        {
            foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !System.Net.IPAddress.IsLoopback(address))
                    return address.ToString();
        }
        catch
        {
            // No network: leave the address out rather than guess.
        }
        return "localhost";
    }
}

/// <summary>The status command: what every server in dedicated.toml is doing.</summary>
static class StatusCommand
{
    public static int Print(DedicatedConfig cfg)
    {
        if (cfg.Servers.Count == 0)
        {
            Console.Error.WriteLine("error: no [[server]] blocks in dedicated.toml");
            return 2;
        }

        int running = 0;
        foreach (var server in cfg.Servers)
        {
            if (!server.Enabled)
            {
                Console.WriteLine($"{server.Name,-24} disabled in dedicated.toml");
                continue;
            }

            var status = LiveStatus.Read(cfg.DataOf(server));
            if (status == null || !status.Fresh)
            {
                Console.WriteLine($"{server.Name,-24} not running   udp {server.Port}");
                continue;
            }

            running++;
            string players = status.Players == 0 ? "no players" : $"{status.Players}/{status.MaxPlayers} players";
            Console.WriteLine($"{server.Name,-24} {status.State,-9} {status.Map} / {status.Mode}   {players}   " +
                              $"udp {status.Port}   pid {status.Pid}");
            if (status.PlaylistTitle != null)
                Console.WriteLine($"{new string(' ', 24)} rotation: {status.PlaylistTitle} ({status.Source})");
            if (status.PlayerNames.Count > 0)
                Console.WriteLine($"{new string(' ', 24)} playing: {string.Join(", ", status.PlayerNames)}");
            Console.WriteLine($"{new string(' ', 24)} dashboard http://127.0.0.1:{status.AdminPort}/  (build {status.Build})");
        }

        Console.WriteLine();
        Console.WriteLine($"{running} of {cfg.Servers.Count(s => s.Enabled)} servers running.");
        return running > 0 ? 0 : 1;
    }
}
