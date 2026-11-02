namespace RenaissanceDedicated;

/// <summary>One game, resolved to what the engine will read.</summary>
sealed class ResolvedEntry
{
    public string Map;       // scenario name, or a path to a mod map
    public string Game;      // a game type name, or a path to a .bin
    public string Variant;   // a path to a .mvar, or null
    public int Weight = 1;
}

/// <summary>
/// Finds the files a playlist or server names, and writes the two files Reach
/// Renaissance's own dedicated server reads: server.ini and a playlist file in
/// the server folder's playlists/ folder.
/// </summary>
sealed class NativeContents
{
    readonly DedicatedConfig _cfg;

    public NativeContents(DedicatedConfig cfg)
    {
        _cfg = cfg;
    }

    /// <summary>
    /// The client's fileshare: the game types and maps you saved in the menus. On
    /// Windows that is the client's own AppData folder; on Linux it lives inside
    /// the Wine or Proton prefix, which client_data in dedicated.toml can name.
    /// </summary>
    public string FileshareRoot => Path.Combine(_cfg.ClientDataRoot ?? "", "fileshare");

    public string FileshareGameType => Path.Combine(FileshareRoot, "GameType");
    public string FileshareMap => Path.Combine(FileshareRoot, "Map");

    /// <summary>The playlists the Reach Renaissance menus wrote, next to your name and settings.</summary>
    public string MenusPlaylists => Path.Combine(_cfg.ClientDataRoot ?? "", "playlists");

    /// <summary>
    /// One game type or saved map: a name the engine resolves itself, or a path to a
    /// file found in the content folder, the client's fileshare or the MCC install.
    /// Returns what to write, or null with a problem naming every folder searched.
    /// </summary>
    public string Resolve(string spec, List<string> problems, bool isMap)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        spec = spec.Trim();

        // A playlist can name a path the menus wrote on Windows (C:\...), or, on
        // Linux, a Z:\ or C:\ path that lives in the Wine prefix. Both name a file
        // this host can open, and the engine is handed back the form it reads.
        // A path that isn't there is looked up by file name in this server folder's
        // own Maps and Game Modes folders first: a server folder copied to a VPS
        // doesn't have the PC it was set up on, but it carries those two folders.
        string local = _cfg.LocalPath(spec);
        bool rooted = Path.IsPathRooted(local);
        if (rooted && File.Exists(local)) return _cfg.EnginePath(local);

        if (!rooted && !LooksLikeAFile(spec))
            return spec;   // a name the engine knows, such as ForgeWorld, Swordbase or Slayer

        string file = Path.GetFileName(spec.Replace('\\', '/'));   // a Windows path still ends in a file name
        var candidates = new List<string>();
        if (!rooted)
        {
            candidates.Add(Path.Combine(_cfg.Root, spec));
            candidates.Add(Path.Combine(_cfg.Content, spec));
        }
        // The portable layout: folders next to dedicated.toml, carried with the
        // server wherever it is copied. Game Modes and GameTypes are the same folder
        // under the two names people use for it.
        string portable = isMap ? Path.Combine(_cfg.Root, "Maps") : Path.Combine(_cfg.Root, "Game Modes");
        candidates.Add(Path.Combine(portable, file));
        if (!isMap)
            candidates.Add(Path.Combine(_cfg.Root, "GameTypes", file));
        candidates.Add(isMap ? Path.Combine(_cfg.Content, "Maps", file) : Path.Combine(_cfg.Content, "Game Modes", file));
        if (!isMap)
            candidates.Add(Path.Combine(_cfg.Content, "GameTypes", file));
        candidates.Add(isMap ? Path.Combine(FileshareMap, file) : Path.Combine(FileshareGameType, file));
        candidates.Add(Path.Combine(_cfg.Content, "Mods", file));
        if (!string.IsNullOrEmpty(_cfg.Mcc))
        {
            candidates.Add(Path.Combine(_cfg.Mcc, "haloreach", isMap ? "maps" : "hopper_game_variants", file));
            if (!isMap)
            {
                candidates.Add(Path.Combine(_cfg.Mcc, "haloreach", "game_variants", file));
                candidates.Add(Path.Combine(_cfg.Mcc, "haloreach", "game_variants", "default", file));
            }
        }

        foreach (var c in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(c))
                return _cfg.EnginePath(c);

        string where = isMap ? "Maps" : "Game Modes";
        problems.Add($"{(isMap ? "map" : "game type")} \"{file}\" " +
                     (rooted ? "isn't at the path the playlist gives and " : "") +
                     $"wasn't found. Put it in the server folder's {where} folder, next to dedicated.toml" +
                     (string.IsNullOrEmpty(_cfg.Mcc)
                         ? $", or set mcc in dedicated.toml to the Halo: The Master Chief Collection folder" +
                           (IsGameContentPath(spec) ? $" (\"{spec}\" is one of the game's own, so it is in MCC)" : "")
                         : ""));
        return null;
    }

    /// <summary>A path inside the game's own install (stock maps and game types).</summary>
    static bool IsGameContentPath(string spec)
    {
        string p = spec.Replace('\\', '/').TrimStart('/');
        return p.StartsWith("hopper_game_variants/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("game_variants/", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("maps/", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeAFile(string spec)
    {
        if (spec.Contains('\\') || spec.Contains('/')) return true;
        string ext = Path.GetExtension(spec);
        return ext.Equals(".bin", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".map", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mvar", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }



    /// <summary>The games a server plays, in order, resolved once for both files.</summary>
    public List<ResolvedEntry> ResolveGames(ServerConfig server, Playlist playlist, List<string> problems)
    {
        var resolved = new List<ResolvedEntry>();
        if (playlist != null)
        {
            foreach (var entry in playlist.Games)
            {
                string game = Resolve(entry.Game, problems, isMap: false);
                string variant = string.IsNullOrEmpty(entry.MapVariant) ? null : Resolve(entry.MapVariant, problems, isMap: true);
                if (game == null || (variant == null && !string.IsNullOrEmpty(entry.MapVariant))) continue;
                if (!CheckRotationName(entry.Map, "map", problems)) continue;
                resolved.Add(new ResolvedEntry { Map = entry.Map, Game = game, Variant = variant, Weight = entry.Weight });
            }
        }
        else
        {
            string game = Resolve(server.GameType, problems, isMap: false);
            string variant = string.IsNullOrEmpty(server.MapVariant) ? null : Resolve(server.MapVariant, problems, isMap: true);
            if (game != null && (variant != null || string.IsNullOrEmpty(server.MapVariant)) && CheckRotationName(server.Map, "map", problems))
                resolved.Add(new ResolvedEntry { Map = server.Map, Game = game, Variant = variant, Weight = 1 });
        }
        return resolved;
    }

    /// <summary>
    /// Does a plain game type name — no path in it — exist as a file the game would
    /// find? Reach Renaissance looks for &lt;MCC&gt;\haloreach\game_variants\&lt;name&gt;.bin,
    /// so "Slayer" only works when that file is there; otherwise the game skips every
    /// rotation entry that names it and refuses to start.
    /// </summary>
    public bool GameTypeNameExists(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string file = name.Trim() + ".bin";
        var dirs = new List<string>
        {
            FileshareGameType,
            Path.Combine(_cfg.Root, "Game Modes"),
            Path.Combine(_cfg.Root, "GameTypes"),
            Path.Combine(_cfg.Content, "Game Modes"),
            Path.Combine(_cfg.Content, "GameTypes"),
        };
        if (!string.IsNullOrEmpty(_cfg.Mcc))
        {
            dirs.Add(Path.Combine(_cfg.Mcc, "haloreach", "game_variants"));
            dirs.Add(Path.Combine(_cfg.Mcc, "haloreach", "games", "game_variants"));
            dirs.Add(Path.Combine(_cfg.Mcc, "haloreach", "hopper_game_variants"));
        }
        foreach (string dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string found in Directory.GetFiles(dir, "*.bin", SearchOption.AllDirectories))
                if (string.Equals(Path.GetFileName(found), file, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        return false;
    }

    /// <summary>Where the game looks for a game type named by a plain name.</summary>
    public string GameTypeLookupHint(string name) => string.IsNullOrEmpty(_cfg.Mcc)
        ? $"<MCC>\\haloreach\\game_variants\\{name}.bin"
        : Path.Combine(_cfg.Mcc, "haloreach", "game_variants", name + ".bin");

    /// <summary>Every game type file this host can see, for the content command.</summary>
    public List<string> ListGameTypes()
    {
        var types = ListFiles(Path.Combine(_cfg.Root, "Game Modes"), "*.bin", Path.Combine(_cfg.Root, "GameTypes"));
        types.AddRange(ListFiles(Path.Combine(_cfg.Content, "Game Modes"), "*.bin", Path.Combine(_cfg.Content, "GameTypes")));
        types.AddRange(ListFiles(FileshareGameType, "*.bin", null));
        if (!string.IsNullOrEmpty(_cfg.Mcc))
            types.AddRange(ListFiles(Path.Combine(_cfg.Mcc, "haloreach", "hopper_game_variants"), "*.bin", null));
        types = types.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        return types;
    }

    /// <summary>Saved Forge maps under content/, the server folder's Maps/ and in the client's fileshare.</summary>
    public List<string> ListMaps()
    {
        var maps = ListFiles(Path.Combine(_cfg.Root, "Maps"), "*.mvar", null);
        maps.AddRange(ListFiles(Path.Combine(_cfg.Root, "Maps"), "*.map", null));
        maps.AddRange(ListFiles(Path.Combine(_cfg.Content, "Maps"), "*.mvar", null));
        maps.AddRange(ListFiles(Path.Combine(_cfg.Content, "Maps"), "*.map", null));
        maps.AddRange(ListFiles(FileshareMap, "*.mvar", null));
        maps = maps.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        return maps;
    }

    /// <summary>Folders under content/Mods holding a custom .map.</summary>
    public List<string> ListMods()
    {
        var mods = new List<string>();
        string dir = Path.Combine(_cfg.Content, "Mods");
        if (!Directory.Exists(dir)) return mods;
        foreach (string sub in Directory.GetDirectories(dir))
            if (Directory.GetFiles(sub, "*.map").Length > 0)
                mods.Add(sub);
        return mods;
    }

    static List<string> ListFiles(string dir, string pattern, string extraDir)
    {
        var found = new List<string>();
        foreach (string d in new[] { dir, extraDir })
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) continue;
            found.AddRange(Directory.GetFiles(d, pattern, SearchOption.AllDirectories));
        }
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    /// <summary>What the content command shows: the folders this server reads.</summary>
    public IEnumerable<string> ContentFolders()
    {
        yield return Path.Combine(_cfg.Root, "Maps");
        yield return Path.Combine(_cfg.Root, "Game Modes");
        yield return Path.Combine(_cfg.Content, "Maps");
        yield return Path.Combine(_cfg.Content, "Game Modes");
    }

    /// <summary>A rotation entry can't name something with a comma, a colon or control characters.</summary>
    static bool CheckRotationName(string value, string what, List<string> problems)
    {
        string problem = Native.NameProblem(value);
        if (problem != null)
        {
            problems.Add($"the {what} \"{value}\" can't be in a rotation: {problem}");
            return false;
        }
        return true;
    }
}

/// <summary>Writes the native files for one server.</summary>
static class Native
{
    /// <summary>
    /// What a name may be inside a playlist file: the engine refuses a name with a
    /// comma, a colon or control characters, and it names the file too.
    /// </summary>
    public static string Name(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name ?? "")
        {
            if (char.IsControl(c)) continue;
            sb.Append(c == ',' ? '-' : c);
        }
        string clean = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (clean.Length > 64) clean = clean.Substring(0, 64).TrimEnd();
        return clean;
    }

    /// <summary>Why the engine would refuse a name, or null when it is fine.</summary>
    public static string NameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "the name is empty";
        if (name.Any(char.IsControl)) return "the name has control characters in it";
        if (name.Contains(',')) return "the name has a comma in it";
        if (name.Contains(':')) return "the name has a colon in it";
        return null;
    }

    /// <summary>
    /// A playlist name that won't collide with a playlist the menus wrote under the
    /// same name: Reach Renaissance looks in its own playlists folder too, and a
    /// name held by both is ambiguous.
    /// </summary>
    public static string FreePlaylistName(string name, string menusPlaylists)
    {
        string clean = Name(name);
        if (clean.Length == 0) return clean;
        if (!string.IsNullOrEmpty(menusPlaylists) && File.Exists(Path.Combine(menusPlaylists, clean + ".txt")))
            return Name(clean + " (dedicated)");
        return clean;
    }

    /// <summary>
    /// server.ini for one server, in the layout Reach Renaissance's own menus write.
    /// </summary>
    public static string ServerIni(ServerConfig server, DedicatedConfig cfg, List<ResolvedEntry> games, string playlistName)
    {
        var defaults = cfg.Defaults;
        var lines = new List<string>
        {
            "# Written by project-renaissance-dedicated from dedicated.toml on each start.",
            "# Change dedicated.toml (or the playlist it names); this file is overwritten.",
            "name = " + server.Name,
        };

        if (playlistName != null)
            lines.Add("playlist = " + playlistName);

        // The rotation: the games this server plays, as Map:game type [|saved map].
        foreach (var entry in games)
        {
            string line = entry.Map + ":" + entry.Game + (entry.Variant != null ? "|" + entry.Variant : "");
            for (int copy = 0; copy < entry.Weight; copy++)
                lines.Add("rotation = " + line);
        }

        lines.Add("time_limit = " + server.TimeLimitOn(defaults));
        // The engine reads public_list = yes as its own --announce-public: a second
        // registration with the built-in server list, on top of the --announce this
        // host passes for a public server. Two registrations for one server on one
        // master fight each other (each heartbeat lands on an id the master has just
        // replaced, so the engine registers again and takes a new id every 30 s),
        // and a player who picks the server from the list is then told it isn't
        // listed. This host does its own announcing; see master in dedicated.toml.
        lines.Add("public_list = no");
        lines.Add("port = " + server.Port);
        lines.Add("lan_address = " + (server.LanOn(defaults) ? "yes" : "no"));
        lines.Add("upnp = " + ((server.Upnp ?? cfg.Upnp) ? "yes" : "no"));
        lines.Add("max_players = " + server.MaxPlayersOn(defaults));
        lines.Add("capacity = " + server.CapacityOn(defaults));
        // The engine's main loop. Its default is 120 rounds a second; on a PC that
        // also plays the game, a lower number leaves CPU for it.
        lines.Add("loop_hz = " + server.LoopHzOn(defaults));
        if (defaults.BigLobby > 0)
            lines.Add("big_lobby = " + (defaults.BigLobby == 64 ? "64" : "off"));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>
    /// The playlist in Reach Renaissance's own text layout, so the dashboard's
    /// playlist list, map voting and weight all work.
    /// </summary>
    public static string PlaylistText(Playlist playlist, List<ResolvedEntry> games)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Written by project-renaissance-dedicated from " + Path.GetFileName(playlist.Path));
        sb.AppendLine("name = " + Name(playlist.Name));
        sb.AppendLine("order = " + playlist.Order);
        sb.AppendLine("vote = " + (playlist.Vote ? "on" : "off"));
        sb.AppendLine("vote_choices = " + playlist.VoteChoices);
        sb.AppendLine("vote_time = " + playlist.VoteTime);
        foreach (var entry in games)
        {
            for (int copy = 0; copy < entry.Weight; copy++)
            {
                sb.AppendLine("game = " + entry.Map + ":" + entry.Game);
                if (entry.Variant != null)
                    sb.AppendLine("map_variant = " + entry.Variant);
            }
        }
        return sb.ToString();
    }
}
