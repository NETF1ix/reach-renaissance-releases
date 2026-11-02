using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>
/// Can players who are not on this network reach each server, or do they come in
/// through the server list's relay — which adds a delay to everything they do?
///
/// Nothing on this machine can answer that alone: it depends on a router and on
/// something outside it. So this asks the server list, the one part of the setup
/// that sits on the internet, and it asks it in three ways:
///
/// * `/v1/whoami` — the address it sees for this machine, nothing more than that;
/// * `/v1/servers` — what it holds for each of these servers, and its own
///   reachability answer when it publishes one;
/// * its **rendezvous** (`RR-RDV1` over UDP) — the same question a player's client
///   asks before it connects. Its answer names the address it would hand that
///   player, and the port in it is the router's answer: the port players are told
///   when something forwards it, a NAT-assigned one when nothing does. That is a
///   real test from outside, so a forward made by hand can be confirmed without
///   another machine.
///
/// When the list answers none of those, the router is what is left (`upnp.log`),
/// and this says which of the three it used instead of dressing a guess up as a
/// test.
/// </summary>
static class Reach
{
    sealed class Listed
    {
        public string Id;         // the listing's own id, for the rendezvous
        public string Name;
        public int Port;
        public bool? Reachable;
        public int? Seen;         // seconds since the list last heard from the server
        public int? Rtt;          // the list's round trip to it
    }

    /// <summary>What the list's rendezvous says it sees, or why it doesn't say.</summary>
    sealed class Outside
    {
        public string Peer;       // "address:port" it would hand a joining player
        public string Note;       // why there is no peer
    }

    static readonly HttpClient Client = NewClient();

    static HttpClient NewClient()
    {
        // A list on your own network is plain http, and one behind an address like
        // sslip.io can have a certificate this machine doesn't know. Nothing private
        // is sent either way: an address in, a public listing out.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
    }

    public static int Print(DedicatedConfig cfg)
    {
        Console.WriteLine($"server list  {cfg.Master}");
        string seenAs;
        try
        {
            seenAs = Field(Get(cfg.Master, "/v1/whoami"), "address");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: couldn't reach the server list at {cfg.Master}: {Short(e)}");
            Console.Error.WriteLine("       Without it there is nothing outside this network to ask. Check the address,");
            Console.Error.WriteLine("       and that this machine is on the internet.");
            return 2;
        }
        Console.WriteLine("this PC      " + (string.IsNullOrEmpty(seenAs) ? "the list didn't say" : seenAs) +
                          "   (the address the list sees)");
        if (IsLocal(cfg.Master))
        {
            Console.WriteLine();
            Console.WriteLine("The server list is on this network, so it only sees your own address: whether players");
            Console.WriteLine("outside can reach a server can't be tested against it. Point master at a public list.");
            return 2;
        }

        List<Listed> listed;
        try
        {
            listed = Parse(Get(cfg.Master, "/v1/servers"));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"error: the server list answered, but its list couldn't be read: {Short(e)}");
            return 2;
        }

        var servers = cfg.Servers.Where(s => s.Enabled).ToList();
        if (servers.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("No servers are enabled in dedicated.toml, so there is nothing to check.");
            return 0;
        }

        bool carrierNat = IsCarrierNat(seenAs);
        string lan = LanAddress();
        int direct = 0, relayed = 0, unknown = 0, privateServers = 0;

        foreach (var server in servers)
        {
            Console.WriteLine();
            Console.WriteLine($"{server.Name}   udp {server.Port}" +
                              (server.PublicOn(cfg.Defaults) ? "" : "   (public = false)"));
            if (!server.PublicOn(cfg.Defaults))
            {
                privateServers++;
                Console.WriteLine("  listed     no: this server is private, so only your own network can join it");
                Console.WriteLine("             nothing outside is involved, so nothing can relay it either");
                continue;
            }

            var entry = listed.FirstOrDefault(l => Same(l.Name, server.Name) && l.Port == server.Port)
                        ?? listed.FirstOrDefault(l => Same(l.Name, server.Name))
                        ?? listed.FirstOrDefault(l => l.Port == server.Port);
            if (entry == null)
            {
                Console.WriteLine("  listed     no: the list doesn't hold this server right now");
                Console.WriteLine("             it is only there while it runs, and a few seconds after it starts");
                continue;
            }

            string heard = entry.Seen is int seen ? $"heard from {seen} s ago" : "just heard from";
            string rtt = entry.Rtt is int rttMs ? $", round trip to it {rttMs} ms" : "";
            Console.WriteLine($"  listed     yes, {heard}{rtt}");

            if (entry.Reachable == true)
            {
                direct++;
                Console.WriteLine("  directly   yes: the list's own check opened a connection to it");
                Console.WriteLine("  players    they connect straight to this PC");
                continue;
            }
            if (entry.Reachable == false)
            {
                relayed++;
                Console.WriteLine("  directly   no: the list's own check could not reach it");
                Console.WriteLine("  players    they come in through the list's relay, which adds a delay to everything");
                Console.WriteLine("  fix        " + Fix(server, lan, carrierNat));
                continue;
            }

            // Nothing published, so ask its rendezvous the question a player's
            // client asks, and use the address it hands back.
            var outside = AskRendezvous(cfg.Master, entry.Id);
            if (outside.Peer != null)
            {
                int seenPort = PortOf(outside.Peer);
                if (seenPort == server.Port)
                {
                    direct++;
                    Console.WriteLine($"  directly   yes: the list's rendezvous sees this server on {outside.Peer},");
                    Console.WriteLine("             the port players are told, so the address it hands out reaches here");
                    Console.WriteLine("  players    they connect straight to this PC");
                    continue;
                }
                relayed++;
                Console.WriteLine($"  directly   no: the list's rendezvous sees this server on {outside.Peer},");
                Console.WriteLine($"             not on {server.Port}, the port the listing tells players to use");
                Console.WriteLine($"  players    a player off your network is only introduced to {seenPort} by the list (a");
                Console.WriteLine("             hole punch), and when that doesn't hold up they come in through its");
                Console.WriteLine("             relay, which is the lag you feel");
                Console.WriteLine("  fix        " + Fix(server, lan, carrierNat));
                continue;
            }

            // No rendezvous answer either: the router is what is left to go on.
            bool? opened = RouterOpened(cfg, server);
            if (opened == true)
            {
                direct++;
                Console.WriteLine("  directly   the list doesn't say; your router has opened the port for it");
                Console.WriteLine("  players    a direct connection should work. A client started with --no-relay");
                Console.WriteLine("             only gets in when it does, which is the plain test.");
                continue;
            }
            unknown++;
            Console.WriteLine("  directly   no evidence: " +
                              (outside.Note ?? $"the list's rendezvous didn't answer on udp {MasterPort(cfg.Master)}"));
            Console.WriteLine("             " + (opened == false
                ? "and nothing opened the port on your router"
                : "and UPnP never asked your router"));
            Console.WriteLine("  players    they can still find it and get in, but only through the list's relay,");
            Console.WriteLine("             which is what makes it feel laggy");
            Console.WriteLine("  fix        " + Fix(server, lan, carrierNat));
        }

        Console.WriteLine();
        var parts = new List<string>();
        if (direct > 0) parts.Add($"{direct} reachable directly");
        if (relayed > 0) parts.Add($"{relayed} not reachable directly");
        if (unknown > 0) parts.Add($"{unknown} with no evidence either way");
        if (privateServers > 0) parts.Add($"{privateServers} private");
        Console.WriteLine($"{servers.Count} server(s): " + string.Join(", ", parts) +
                          (carrierNat ? "   (this connection is behind carrier NAT: forwarding can't help)" : ""));
        return 0;
    }

    static string Fix(ServerConfig server, string lan, bool carrierNat)
    {
        if (carrierNat)
            return "this connection's internet address belongs to the carrier (100.64.0.0/10), so no " +
                   "router forward can be reached from outside; the list's relay is the only way in here";
        string to = string.IsNullOrEmpty(lan) ? "this PC" : lan;
        return $"forward UDP {server.Port} on the router to {to}, then run reach again: it " +
               $"should see the server on {server.Port}";
    }

    /// <summary>
    /// Where the server list sees one of these servers from outside. This is the
    /// question a player's client asks before it connects: `RR-RDV1` and a JSON
    /// "join" over UDP to the list's rendezvous port (the one in `master`), and
    /// back comes "punch", whose `peer` is the address it would hand the player.
    /// </summary>
    static Outside AskRendezvous(string master, string id)
    {
        var outside = new Outside();
        if (string.IsNullOrEmpty(id))
        {
            outside.Note = "the list gave this server no id to ask about";
            return outside;
        }
        if (!Uri.TryCreate(master, UriKind.Absolute, out var uri))
        {
            outside.Note = "the server list's address couldn't be read";
            return outside;
        }

        IPAddress address = null;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            address = literal;
        }
        else
        {
            try
            {
                address = Dns.GetHostAddresses(uri.Host)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            catch (Exception e)
            {
                outside.Note = $"the server list's name didn't resolve ({Short(e)})";
                return outside;
            }
        }
        if (address == null)
        {
            outside.Note = "the server list's name has no IPv4 address";
            return outside;
        }

        string nonce = "reach-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        string text = "RR-RDV1\n" + JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["t"] = "join",
            ["id"] = id,
            ["nonce"] = nonce,
        });
        byte[] payload = Encoding.UTF8.GetBytes(text);
        var to = new IPEndPoint(address, uri.Port);

        try
        {
            using var socket = new UdpClient();
            socket.Client.ReceiveTimeout = 900;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                socket.Send(payload, payload.Length, to);
                for (int wait = 0; wait < 2; wait++)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] reply;
                    try
                    {
                        reply = socket.Receive(ref from);
                    }
                    catch (SocketException)
                    {
                        break;      // this wait ran out; send again
                    }
                    if (ReadRendezvous(reply, nonce, outside))
                        return outside;
                }
            }
        }
        catch (SocketException e)
        {
            outside.Note = $"the server list's rendezvous couldn't be asked ({e.SocketErrorCode})";
            return outside;
        }

        if (outside.Note == null)
            outside.Note = $"the server list's rendezvous didn't answer on udp {uri.Port}";
        return outside;
    }

    /// <summary>One rendezvous datagram: "punch" with the address, or "refused" with a reason.</summary>
    static bool ReadRendezvous(byte[] reply, string nonce, Outside outside)
    {
        string text = Encoding.UTF8.GetString(reply);
        int head = text.IndexOf('\n');
        if (head < 0 || !text.StartsWith("RR-RDV1", StringComparison.Ordinal)) return false;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text[(head + 1)..]);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            switch (Text(root, "t"))
            {
                case "punch":
                    // Its answer to someone else's join can land on this socket too.
                    if (!string.Equals(Text(root, "nonce"), nonce, StringComparison.Ordinal)) return false;
                    outside.Peer = Text(root, "peer");
                    if (outside.Peer == null) outside.Note = "the list's rendezvous named no address";
                    return true;
                case "refused":
                    outside.Note = Text(root, "reason") ?? "the list refused to introduce it";
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>The port the rendezvous is asked on: the one in `master`.</summary>
    static int MasterPort(string master) =>
        Uri.TryCreate(master, UriKind.Absolute, out var uri) ? uri.Port : 0;

    /// <summary>The port out of "address:port" (and out of "[v6]:port").</summary>
    static int PortOf(string endpoint)
    {
        int colon = endpoint.LastIndexOf(':');
        return colon > 0 && int.TryParse(endpoint[(colon + 1)..], out int port) ? port : 0;
    }

    /// <summary>Did the router open this server's port, from what its UPnP keeper wrote?</summary>
    static bool? RouterOpened(DedicatedConfig cfg, ServerConfig server)
    {
        if (!(server.Upnp ?? cfg.Upnp)) return null;
        try
        {
            string log = Path.Combine(cfg.DataOf(server), "upnp.log");
            if (!File.Exists(log)) return null;
            string text = File.ReadAllText(log);
            if (text.Contains("no Internet gateway answered")) return false;
            return text.Trim().Length == 0 ? null : true;
        }
        catch
        {
            return null;
        }
    }

    static string Get(string master, string path)
    {
        using var response = Client.GetAsync(master.TrimEnd('/') + path).GetAwaiter().GetResult();
        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode} for {path}");
        return body;
    }

    /// <summary>The list's JSON, read with its shape in mind and nothing else guessed.</summary>
    static List<Listed> Parse(string json)
    {
        var list = new List<Listed>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("servers", out var servers) || servers.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in servers.EnumerateArray())
        {
            if (!item.TryGetProperty("signed", out var signed) ||
                !signed.TryGetProperty("listing", out var listing)) continue;
            var entry = new Listed
            {
                Id = Text(item, "id"),
                Name = Text(listing, "name"),
                Port = Number(listing, "port") ?? 0,
                Reachable = item.TryGetProperty("reachable", out var reachable)
                    ? reachable.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => null,
                    }
                    : null,
                Seen = Number(item, "last_seen_seconds"),
                Rtt = Number(item, "host_rtt_ms"),
            };
            list.Add(entry);
        }
        return list;
    }

    static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32() : null;

    static string Field(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return Text(doc.RootElement, name);
    }

    static bool Same(string a, string b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    static bool IsLocal(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.IsLoopback || IsPrivate(uri.Host));

    /// <summary>This PC's own address on the network, for the forwarding instruction.</summary>
    static string LanAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = address.Address.ToString();
                    if (ip.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                    if (IsPrivate(ip)) return ip;
                }
            }
        }
        catch
        {
            // An address is a nicety in the message, never a reason to fail.
        }
        return null;
    }

    static bool IsPrivate(string ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed)) return false;
        byte[] b = parsed.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || b[0] == 127;
    }

    static bool IsCarrierNat(string ip)
    {
        if (!IPAddress.TryParse(ip, out var parsed)) return false;
        byte[] b = parsed.GetAddressBytes();
        return b.Length == 4 && b[0] == 100 && b[1] >= 64 && b[1] <= 127;
    }

    static string Short(Exception e)
    {
        var inner = e is AggregateException aggregate ? aggregate.GetBaseException() : e;
        return inner is HttpRequestException http && http.InnerException != null
            ? http.InnerException.Message
            : inner.Message;
    }
}
