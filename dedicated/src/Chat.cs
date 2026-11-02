using System.Text;
using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>
/// The game's own chat. Reach Renaissance relays chat between the players in a
/// match through the engine, and reads one file for messages that come from the
/// server itself: cwd/match-chat-admin.json. A message there is stamped with the
/// match's epoch (the engine drops one from an older match) and appears in the
/// players' in-game chat, which is what an announcement is.
/// </summary>
static class Chat
{
    /// <summary>What the engine says about its chat right now.</summary>
    public sealed class State
    {
        public bool Ready;          // a match's chat is up
        public string Room;
        public string Epoch;        // the match's stamp: an admin message must carry it
        public string Session;
        public string Error;
    }

    public static State Read(string serverDir)
    {
        var state = new State();
        try
        {
            string file = Path.Combine(serverDir, "cwd", "match-chat.json");
            if (!File.Exists(file)) return state;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            state.Ready = root.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True;
            state.Room = Text(root, "room");
            state.Epoch = Text(root, "epoch");
            state.Session = Text(root, "session");
            state.Error = Text(root, "error");
        }
        catch
        {
            // Mid-write: the next call reads it.
        }
        return state;
    }

    public sealed class Result
    {
        public bool Session;        // a match with chat was up, so the message has somewhere to go
        public string Note;         // one line for the console, the log and the RCON reply
    }

    /// <summary>
    /// Puts one line into the players' chat. Returns what actually happened: the
    /// engine only relays when a match is up, so an announcement while the server
    /// is empty is kept in the log rather than pretend it reached anyone.
    /// </summary>
    public static Result Announce(string serverDir, string text)
    {
        var state = Read(serverDir);
        string message = OneLine(text);
        if (message.Length == 0)
            return new Result { Note = "nothing to announce" };

        var result = new Result { Session = state.Ready };
        try
        {
            string dir = Path.Combine(serverDir, "cwd");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "match-chat-admin.json");
            // request: a fresh id per message, so the engine can't read one twice.
            // epoch: the match this is meant for, so a queued message from an older
            // match is dropped rather than shown in the wrong game.
            // The command is the engine's own nested shape; a flat one is read and
            // dropped, which leaves an owner believing their players saw it.
            var payload = new Dictionary<string, object>
            {
                ["request"] = Guid.NewGuid().ToString("N"),
                ["version"] = 1,
                ["epoch"] = state.Epoch ?? "",
                ["command"] = new Dictionary<string, object>
                {
                    ["say"] = new Dictionary<string, object>
                    {
                        ["text"] = message,
                        ["channel"] = "all",
                    },
                },
            };
            string json = JsonSerializer.Serialize(payload) + "\n";
            string temp = file + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, file, overwrite: true);
            // The engine takes the file and clears it, so a message it can't show is
            // gone: say plainly whether players saw it, and why not when they didn't.
            string why = string.IsNullOrWhiteSpace(state.Error)
                ? "the engine's match chat isn't up (no online session)"
                : state.Error.Trim();
            result.Note = state.Ready
                ? "announced: " + message
                : $"announced: {message} — nobody saw it: {why}";
        }
        catch (Exception e)
        {
            result.Note = "couldn't write the announcement: " + e.Message;
        }
        return result;
    }

    /// <summary>Chat is one line: newlines and control characters would break the relay.</summary>
    static string OneLine(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in (text ?? "").Trim())
            sb.Append(c < 32 || c == 127 ? ' ' : c);
        string line = sb.ToString().Trim();
        while (line.Contains("  ")) line = line.Replace("  ", " ");
        return line.Length > 200 ? line.Substring(0, 200) : line;
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
