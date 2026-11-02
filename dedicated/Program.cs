namespace RenaissanceDedicated;

static class Program
{
    const string Usage = @"Project Renaissance dedicated servers for Halo: Reach

  project-renaissance-dedicated [command] [options]

  (nothing)              start every server in dedicated.toml (same as ""dedicated"")
  dedicated              start every server in dedicated.toml
  check                  check the config, content, playlists and ports and change nothing
  content [words]        list the maps and game types a playlist can name
  reach                  ask the server list whether players reach each server
                         directly, or through its relay
  status                 what every server in dedicated.toml is doing
  update                 is a newer game release available?
  update --apply         download and install it (stop the servers first)
  playlist show <file>   print a .playlist.json
  playlist import <file> turn a playlist the game's menus wrote into a .playlist.json
  playlist new <name>    make one: --maps ""ForgeWorld"" --games ""Slayer""

Options:
  --config <file>        the file to read (default: dedicated.toml next to this program)
  --dry-run              with a start: check every server, print what would run, change nothing
  --apply                with update: download and install the newer release
  --verbose              also show the engine's own output in this window
  --out <file>           where playlist import and playlist new write
  --maps / --games       comma-separated lists for playlist new
  --help                 this text
  --version              the version

First run: opened in a folder with no dedicated.toml, this builds the folders
(game, content, playlists, data, logs), writes a starter dedicated.toml and the
example playlist, and copies a nearby OmniVault desktop app into panel/.

Every server in dedicated.toml gets a folder under data/: its server.ini, its playlist,
its log and status.json, and the admin dashboard's own state. Press Ctrl+C to stop.

On Linux, Reach Renaissance's engine is still a Windows program: this host starts it
through Wine, or through the command in runner (Proton, a wrapper script). See README.md.";

    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ConfigError e)
        {
            Console.Error.WriteLine(e.Line > 0
                ? $"error: {Path.GetFileName(e.File)} line {e.Line}: {e.Message}"
                : $"error: {e.Message}");
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 2;
        }
    }

    static int Run(string[] args)
    {
        string configPath = null, command = null, mapList = null, gameList = null, outPath = null;
        bool dryRun = false, verbose = false, apply = false;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                case "help":
                    command ??= "help";
                    break;
                case "--version":
                    command ??= "version";
                    break;
                case "--dry-run":
                case "--what-if":
                    dryRun = true;
                    break;
                case "--verbose":
                case "-v":
                    verbose = true;
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--config":
                    configPath = Next(args, ref i, arg);
                    break;
                case "--maps":
                    mapList = Next(args, ref i, arg);
                    break;
                case "--games":
                    gameList = Next(args, ref i, arg);
                    break;
                case "--out":
                    outPath = Next(args, ref i, arg);
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) || arg.StartsWith('-') && arg.Length > 1)
                        throw new ConfigError("the command line", 0, $"unknown option {arg} (--help lists them)");
                    if (command == null) command = arg;
                    else rest.Add(arg);
                    break;
            }
        }

        int exitCode = command switch
        {
            null => 0,       // filled in below: starting servers
            "help" => Help(),
            "version" => Version(),
            _ => 0,
        };
        if (exitCode != 0 || command is "help" or "version")
            return exitCode;

        // Opened in a folder with no dedicated.toml: build the layout first, the
        // way a hosting box needs it, instead of failing with "no dedicated.toml".
        if (configPath == null && Setup.IsFirstRun())
            return Setup.Run();

        string config = FindConfig(configPath);

        switch ((command ?? "dedicated").ToLowerInvariant())
        {
            case "dedicated":
            case "start":
                return Host.Start(DedicatedConfig.Load(config), dryRun, verbose);
            case "check":
                return CheckCommand.Print(DedicatedConfig.Load(config));
            case "reach":
                return Reach.Print(DedicatedConfig.Load(config));
            case "content":
                return ContentCommand.Print(DedicatedConfig.Load(config), rest);
            case "status":
                return StatusCommand.Print(DedicatedConfig.Load(config));
            case "update":
                return UpdateCommand.Print(DedicatedConfig.Load(config), apply);
            case "playlist":
                if (rest.Count == 0)
                    throw new ConfigError("the command line", 0, "playlist needs show, import or new");
                return rest[0].ToLowerInvariant() switch
                {
                    "show" when rest.Count >= 2 => PlaylistCommand.Show(rest[1]),
                    "import" when rest.Count >= 2 => PlaylistCommand.Import(rest[1], rest.Count >= 3 ? rest[2] : outPath),
                    "new" when rest.Count >= 2 => PlaylistCommand.New(rest[1], mapList, gameList, outPath),
                    "show" or "import" or "new" => throw new ConfigError("the command line", 0, $"playlist {rest[0]} needs a file name"),
                    _ => throw new ConfigError("the command line", 0, $"playlist {rest[0]} isn't a command (show, import, new)"),
                };
            default:
                Console.Error.WriteLine($"error: {command} isn't a command");
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
            throw new ConfigError("the command line", 0, $"{option} needs a value");
        return args[++i];
    }

    /// <summary>dedicated.toml: --config, else next to this program, else the current folder.</summary>
    static string FindConfig(string configPath)
    {
        if (!string.IsNullOrEmpty(configPath))
        {
            if (!File.Exists(configPath))
                throw new ConfigError(configPath, 0, "there is no file there");
            return configPath;
        }
        foreach (string candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "dedicated.toml"),
                     Path.Combine(Directory.GetCurrentDirectory(), "dedicated.toml"),
                 })
            if (File.Exists(candidate))
                return candidate;

        throw new ConfigError("dedicated.toml", 0,
            $"no dedicated.toml next to this program ({AppContext.BaseDirectory}) or in the current folder; " +
            "pass --config <file>");
    }

    static int Help()
    {
        Console.WriteLine(Usage);
        return 0;
    }

    static int Version()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        Console.WriteLine($"project-renaissance-dedicated {version?.Major}.{version?.Minor}.{version?.Build}");
        return 0;
    }
}
