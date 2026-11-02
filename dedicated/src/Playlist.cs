using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>One game in a playlist: what plays and how likely it is to be picked.</summary>
sealed class PlaylistEntry
{
    public string Map;          // a scenario: ForgeWorld, or a mod map's scenario name
    public string Game;         // a game type name, or a path to a .bin variant
    public string MapVariant;   // optional path to a saved Forge map (.mvar)
    public int Weight = 1;      // in a shuffle, this many copies of the entry
    public int Line;            // where it is in the file, for messages
}

/// <summary>A Reclaimer-style playlist file, holding games a server votes on.</summary>
sealed class Playlist
{
    public string Path;
    public string Name;
    public string Order = "shuffle";   // shuffle or turn
    public bool Vote = true;
    public int VoteChoices = 10;
    public int VoteTime = 20;
    public List<PlaylistEntry> Games = new();
    public List<string> Warnings = new();

    /// <summary>Reads a .playlist.json. Unknown keys are reported, not refused.</summary>
    public static Playlist Load(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string text;
        try
        {
            text = File.ReadAllText(full);
        }
        catch (Exception e)
        {
            throw new ConfigError(full, 0, $"can't read the playlist: {e.Message}");
        }

        var p = new Playlist { Path = full };
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException e)
        {
            throw new ConfigError(full, (int)(e.LineNumber ?? 0) + 1, $"not valid JSON: {e.Message}");
        }

        using (doc)
        {
            var rootElement = doc.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object)
                throw new ConfigError(full, 1, "a playlist is a JSON object: { \"name\": ..., \"games\": [ ... ] }");

            var known = new HashSet<string>(StringComparer.Ordinal)
            {
                "version", "name", "order", "vote", "vote_seconds", "vote_time", "vote_choices", "games",
            };
            foreach (var prop in rootElement.EnumerateObject())
                if (!known.Contains(prop.Name))
                    p.Warnings.Add($"games' file: {prop.Name} isn't a playlist setting this host knows; ignored");

            p.Name = GetString(rootElement, "name", full) ?? System.IO.Path.GetFileNameWithoutExtension(full);
            p.Order = (GetString(rootElement, "order", full) ?? "shuffle").ToLowerInvariant();
            if (p.Order != "shuffle" && p.Order != "turn")
                throw new ConfigError(full, 1, "order must be \"shuffle\" or \"turn\"");
            p.Vote = GetBool(rootElement, "vote", full) ?? true;
            long? voteTime = GetNumber(rootElement, "vote_time", full) ?? GetNumber(rootElement, "vote_seconds", full);
            if (voteTime.HasValue) p.VoteTime = (int)voteTime.Value;
            long? voteChoices = GetNumber(rootElement, "vote_choices", full);
            if (voteChoices.HasValue) p.VoteChoices = (int)voteChoices.Value;

            if (!rootElement.TryGetProperty("games", out var games) || games.ValueKind != JsonValueKind.Array)
                throw new ConfigError(full, 1, "the playlist needs a \"games\" array");
            if (games.GetArrayLength() == 0)
                p.Warnings.Add("the playlist has no games");

            int i = 0;
            foreach (var g in games.EnumerateArray())
            {
                i++;
                if (g.ValueKind != JsonValueKind.Object)
                    throw new ConfigError(full, 1, $"games[{i}] must be an object like {{ \"map\": \"ForgeWorld\", \"game\": \"Slayer\" }}");
                var entry = new PlaylistEntry { Line = i };
                var knownEntry = new HashSet<string>(StringComparer.Ordinal) { "map", "game", "map_variant", "weight" };
                foreach (var prop in g.EnumerateObject())
                    if (!knownEntry.Contains(prop.Name))
                        p.Warnings.Add($"games[{i}]: {prop.Name} isn't a key this host knows; ignored");

                entry.Map = GetString(g, "map", full) ?? "";
                entry.Game = GetString(g, "game", full) ?? "";
                entry.MapVariant = GetString(g, "map_variant", full) ?? "";
                long? weight = GetNumber(g, "weight", full);
                entry.Weight = (int)(weight ?? 1);

                if (string.IsNullOrWhiteSpace(entry.Map))
                    throw new ConfigError(full, 1, $"games[{i}] has no map");
                if (string.IsNullOrWhiteSpace(entry.Game))
                    throw new ConfigError(full, 1, $"games[{i}] has no game");
                if (entry.Weight < 1 || entry.Weight > 20)
                    throw new ConfigError(full, 1, $"games[{i}].weight must be 1 through 20");
                p.Games.Add(entry);
            }
        }

        return p;
    }

    static string GetString(JsonElement e, string name, string file)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        throw new ConfigError(file, 1, $"{name} must be text");
    }

    static bool? GetBool(JsonElement e, string name, string file)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        throw new ConfigError(file, 1, $"{name} must be true or false");
    }

    static long? GetNumber(JsonElement e, string name, string file)
    {
        if (!e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetInt64();
        throw new ConfigError(file, 1, $"{name} must be a number");
    }
}
