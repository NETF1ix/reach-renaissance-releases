using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RenaissanceDedicated;

/// <summary>
/// What a command can ask of one running server. The same object backs every
/// client: the host itself, and any RCON panel an owner points at it.
/// </summary>
interface IRconHost
{
    string Name { get; }
    string RconStatus();
    string RconPlayers();
    string RconSay(string text);
    string RconKick(string target, string reason);
    string RconBan(string target, string duration, string reason);
    string RconUnban(string target);
    string RconBans();
    string RconEndGame();
    string RconEndRound();
    string RconMaps();
    string RconModes();
    string RconLoad(string map, string mode, bool now);
    string RconNext(string game);
    string RconRestart(string when);
    string RconReload();
}

/// <summary>
/// Source RCON: the protocol admin panels speak (Valve's, as Halo server tools
/// use it). One packet is a little-endian length, a request id, a type, the
/// body, and two null bytes; a client authenticates with type 3, then sends
/// commands as type 2 and reads type 0 replies. The request id echoes back, and
/// a command packet with an empty body means "that was everything".
/// </summary>
sealed class RconListener : IDisposable
{
    const int Auth = 3, AuthReply = 2, Command = 2, Reply = 0;
    const int MaxPacket = 65536;

    readonly TcpListener _listener;
    readonly Thread _thread;
    readonly string _password;
    readonly string _where;
    readonly IRconHost _host;
    readonly bool _everyAddress;             // bound to 0.0.0.0: no address of its own
    volatile bool _stopping;

    public string Address { get; }
    public int Port { get; }

    public static RconListener Start(IRconHost host, string bind, int port, string password, out string problem)
    {
        problem = null;
        IPAddress address;
        if (bind == "*" || bind == "any" || bind == "0.0.0.0")
        {
            address = IPAddress.Any;
        }
        else if (!IPAddress.TryParse(bind, out address))
        {
            try
            {
                address = Dns.GetHostAddresses(bind).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                          ?? IPAddress.Loopback;
            }
            catch
            {
                problem = $"{bind} isn't an address this PC can listen on";
                return null;
            }
        }

        try
        {
            var listener = new RconListener(host, address, port, password);
            return listener;
        }
        catch (Exception e)
        {
            problem = $"{address}:{port} couldn't be opened: {e.Message}";
            return null;
        }
    }

    RconListener(IRconHost host, IPAddress address, int port, string password)
    {
        _host = host;
        _password = password;
        _everyAddress = address.Equals(IPAddress.Any);
        _listener = new TcpListener(address, port);
        _listener.Start();
        Address = address.ToString();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _where = $"{Address}:{Port}";
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = $"rcon {_host.Name}" };
        _thread.Start();
    }

    void AcceptLoop()
    {
        while (!_stopping)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch
            {
                return;   // stopped, or the socket went away with the server
            }
            var thread = new Thread(() => Serve(client)) { IsBackground = true };
            thread.Start();
        }
    }

    void Serve(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 20000;
                client.SendTimeout = 20000;
                using var stream = client.GetStream();
                bool allowed = false;
                var header = new byte[4];
                while (!_stopping)
                {
                    if (!ReadExact(stream, header, 4)) return;
                    int size = BitConverter.ToInt32(header, 0);
                    if (size < 10 || size > MaxPacket) return;
                    var packet = new byte[size];
                    if (!ReadExact(stream, packet, size)) return;
                    int id = BitConverter.ToInt32(packet, 0);
                    int type = BitConverter.ToInt32(packet, 4);
                    string text = Encoding.UTF8.GetString(packet, 8, size - 10);

                    if (type == Auth)
                    {
                        allowed = text == _password;
                        // An accepted password answers with the request's own id; a
                        // refused one answers -1, which clients read as "wrong password".
                        Send(stream, allowed ? id : -1, AuthReply, allowed ? "" : "authentication failed");
                        if (!allowed) return;
                        continue;
                    }

                    if (type == Command)
                    {
                        if (!allowed)
                        {
                            Send(stream, -1, Reply, "authenticate first");
                            return;
                        }
                        string reply = text.Length == 0 ? "" : Dispatch(text);
                        Send(stream, id, Reply, reply);
                        Send(stream, id, Reply, "");   // the usual end-of-response packet
                        continue;
                    }
                }
            }
        }
        catch
        {
            // A panel that hangs up mid-answer is not this server's problem.
        }
    }

    /// <summary>One command line, answered with the text the panel shows.</summary>
    string Dispatch(string line)
    {
        string verb = line;
        string rest = "";
        int space = line.IndexOf(' ');
        if (space > 0)
        {
            verb = line.Substring(0, space);
            rest = line.Substring(space + 1).Trim();
        }

        try
        {
            switch (verb.ToLowerInvariant())
            {
                case "status":
                    return _host.RconStatus();
                case "players":
                case "who":
                    return _host.RconPlayers();
                case "say":
                case "announce":
                case "broadcast":
                    return rest.Length == 0 ? "usage: say <message>" : _host.RconSay(rest);
                case "kick":
                    {
                        var (target, kickWhy) = SplitOnce(rest);
                        return target.Length == 0 ? "usage: kick <player> [reason]" : _host.RconKick(target, kickWhy);
                    }
                case "ban":
                    if (!RconCommands.ParseBan(rest, out string who, out string howLong, out string why, out string bad))
                        return bad;
                    return _host.RconBan(who, howLong, why);
                case "unban":
                    {
                        var unbanArgs = Args(rest);
                        return unbanArgs.Count == 0 ? "usage: unban <player id or address>" : _host.RconUnban(unbanArgs[0]);
                    }
                case "bans":
                case "banlist":
                    return _host.RconBans();
                case "end_game":
                case "endgame":
                    return _host.RconEndGame();
                case "end_round":
                case "endround":
                    return _host.RconEndRound();
                case "maps":
                case "maplist":
                    return _host.RconMaps();
                case "modes":
                case "gametypes":
                    return _host.RconModes();
                case "load":
                case "map":
                    {
                        var (map, mode) = SplitOnce(rest);
                        return _host.RconLoad(map, mode, now: true);
                    }
                case "nextmap":
                    {
                        var (map, mode) = SplitOnce(rest);
                        return _host.RconLoad(map, mode, now: false);
                    }
                case "next":
                    return rest.Length == 0
                        ? "usage: next <map:game>, or next now <map:game> to end this game and play it"
                        : _host.RconNext(rest);
                case "restart":
                    return _host.RconRestart(rest);
                case "reload":
                case "reload_config":
                    return _host.RconReload();
                case "help":
                case "?":
                    return Help();
                default:
                    return $"unknown command \"{verb}\". Try: help";
            }
        }
        catch (Exception e)
        {
            return "that command failed: " + e.Message;
        }
    }

    public static string Help() =>
        "status | players   what this server is doing, and who is in it\n" +
        "say <message>      put a message in the game's chat (an announcement)\n" +
        "kick <player> [reason]\n" +
        "ban <player or address> [30m|12h|7d|2w] [reason]\n" +
        "unban <player or address> | bans\n" +
        "endgame | endround | next [now] <map:game> | restart [now|empty|game_end]\n" +
        "maps | modes       list the maps and game types this server can play\n" +
        "load <map> [mode]  play it now; nextmap queues it after the current game\n" +
        "reload             re-read dedicated.toml and restart what changed\n" +
        "A player is a name, or the id from players (16 hex digits, with or without\n" +
        "the leading zeros a panel shows).";

    static (string, string) SplitOnce(string rest)
    {
        var parts = Args(rest);
        if (parts.Count == 0) return ("", "");
        return (parts[0], string.Join(' ', parts.Skip(1)));
    }

    /// <summary>
    /// Splits a command's arguments the way a panel writes them: double quotes
    /// group a name or path with spaces, and the quotes are not part of either.
    /// </summary>
    static List<string> Args(string rest)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool escaped = false;
        foreach (char c in rest ?? "")
        {
            if (escaped) { current.Append(c); escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true; continue; }
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    static bool ReadExact(NetworkStream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int got;
            try
            {
                got = stream.Read(buffer, read, count - read);
            }
            catch
            {
                return false;
            }
            if (got <= 0) return false;
            read += got;
        }
        return true;
    }

    static void Send(NetworkStream stream, int id, int type, string text)
    {
        // size counts the id, the type, the body and the two null bytes that end
        // every packet; a client reads exactly that many bytes and waits for the
        // rest, so both nulls have to be written.
        byte[] body = Encoding.UTF8.GetBytes(text ?? "");
        var packet = new byte[14 + body.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(0, 4), 10 + body.Length);
        BitConverter.TryWriteBytes(packet.AsSpan(4, 4), id);
        BitConverter.TryWriteBytes(packet.AsSpan(8, 4), type);
        body.CopyTo(packet, 12);
        stream.Write(packet, 0, packet.Length);
    }

    public void Dispose()
    {
        _stopping = true;
        try
        {
            _listener.Stop();
        }
        catch
        {
            // Already gone.
        }
    }

    /// <summary>
    /// Where a client connects. A listener on every address has no address of its
    /// own, and "0.0.0.0" is not one a panel can use, so it reports this PC's.
    /// </summary>
    public override string ToString() => _everyAddress ? $"{LiveStatus.ServerAddress()}:{Port}" : _where;
}

static class RconCommands
{
    /// <summary>
    /// ban &lt;player or address&gt; [30m|12h|7d|2w] [reason]. The reason may arrive as a
    /// quoted string, which is what JSON panels send, so quotes are stripped and
    /// quoted text stays one argument.
    /// </summary>
    public static bool ParseBan(string rest, out string target, out string duration, out string reason, out string problem)
    {
        target = duration = reason = "";
        problem = "usage: ban <player or address> [30m|12h|7d|2w] [reason]";

        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in rest)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { parts.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString());
        if (parts.Count == 0) return false;

        target = parts[0];
        for (int i = 1; i < parts.Count; i++)
        {
            string part = parts[i];
            if (duration.Length == 0 && IsDuration(part)) duration = part;
            else reason = reason.Length == 0 ? part : reason + " " + part;
        }
        problem = null;
        return true;
    }

    public static bool IsDuration(string text)
    {
        if (text.Length < 2) return false;
        char unit = char.ToLowerInvariant(text[^1]);
        return (unit == 'm' || unit == 'h' || unit == 'd' || unit == 'w') && text[..^1].All(char.IsDigit);
    }

    /// <summary>A duration the engine takes as minutes.</summary>
    public static int? Minutes(string text)
    {
        if (!IsDuration(text)) return null;
        char unit = char.ToLowerInvariant(text[^1]);
        if (!int.TryParse(text[..^1], out int n)) return null;
        return unit switch
        {
            'm' => n,
            'h' => n * 60,
            'd' => n * 60 * 24,
            'w' => n * 60 * 24 * 7,
            _ => null,
        };
    }
}
