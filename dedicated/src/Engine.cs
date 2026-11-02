using System.Diagnostics;
using System.Text;

namespace RenaissanceDedicated;

/// <summary>
/// How this host starts the dedicated server engine. On Windows it runs
/// bin\host-probe.exe. On Linux, if the game ships a native bin/host-probe that
/// runs, otherwise the Windows engine through Wine or a runner you name (a
/// Proton command, a wrapper script). Every path the engine reads is written in
/// the form it understands, which under Wine means Z:\ paths.
/// </summary>
sealed class EngineLauncher
{
    public string Program;                    // what this host starts
    public List<string> RunnerArgs = new();   // arguments before the engine, for a runner
    public string EngineProgram;              // the engine, when a runner starts it
    public bool UnderWine;                    // the engine is a Windows program
    public string WinePrefix;
    public string Problem;                    // why the engine can't run here, or null

    public bool Ok => Problem == null;

    public static EngineLauncher Resolve(DedicatedConfig cfg)
    {
        var launcher = new EngineLauncher { WinePrefix = cfg.WinePrefix };
        string game = cfg.Game ?? "";
        string windows = Path.Combine(game, "bin", "host-probe.exe");
        string native = Path.Combine(game, "bin", "host-probe");

        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game))
        {
            launcher.Program = windows;
            launcher.Problem = $"the game folder isn't there: {game}. Copy the Reach Renaissance files (bin, data, " +
                               "themes, licenses, VERSION.txt - the launcher .exe isn't needed) into that folder, " +
                               "or point game in dedicated.toml at the folder you have.";
            return launcher;
        }

        if (Platform.Windows)
        {
            launcher.Program = windows;
            if (!File.Exists(windows))
                launcher.Problem = $"{windows} isn't there. Copy the Reach Renaissance files into {game} " +
                                   "(bin, data, themes, licenses, VERSION.txt) or point game in dedicated.toml at the " +
                                   "folder holding bin\\host-probe.exe.";
            return launcher;
        }

        // Linux and macOS: a native engine, if the game ever ships one, needs no help.
        if (File.Exists(native))
        {
            launcher.Program = native;
            return launcher;
        }

        if (!File.Exists(windows))
        {
            launcher.Program = native;
            launcher.Problem = $"neither {native} nor {windows} is there. Copy the Reach Renaissance files into " +
                               $"{game} (bin, data, themes, licenses, VERSION.txt), or point game in dedicated.toml " +
                               "at the folder holding bin/host-probe.";
            return launcher;
        }

        // The game is a Windows program on this machine, so is its engine.
        launcher.UnderWine = true;
        launcher.EngineProgram = windows;

        if (!string.IsNullOrWhiteSpace(cfg.Runner))
        {
            var parts = SplitCommandLine(cfg.Runner);
            launcher.Program = ExpandHome(parts.Count > 0 ? parts[0] : "");
            launcher.RunnerArgs = parts.Skip(1).ToList();
            if (string.IsNullOrWhiteSpace(launcher.Program))
            {
                launcher.Program = "wine";
                launcher.Problem = "runner in dedicated.toml is empty; name the command that starts the Windows engine, such as \"wine\".";
            }
            return launcher;
        }

        string wine = FindOnPath("wine") ?? FindOnPath("wine64");
        launcher.Program = wine ?? "wine";
        if (wine == null)
            launcher.Problem = "Reach Renaissance's engine is a Windows program and Wine isn't installed. " +
                               "Install Wine, or set runner in dedicated.toml to the command you use (a Proton command " +
                               "or a wrapper script). See the Linux notes in README.md.";
        return launcher;
    }

    /// <summary>The path as the engine must see it: Z:\ under Wine, unchanged otherwise.</summary>
    public string AsEnginePath(string path) =>
        UnderWine && !string.IsNullOrWhiteSpace(path) ? Platform.ToWindowsPath(path) : path;

    /// <summary>Everything to start: the runner (if any), the engine, then the engine's arguments.</summary>
    public List<string> FullArgs(IEnumerable<string> engineArgs)
    {
        var args = new List<string>(RunnerArgs);
        if (!string.IsNullOrEmpty(EngineProgram)) args.Add(AsEnginePath(EngineProgram));
        args.AddRange(engineArgs);
        return args;
    }

    public ProcessStartInfo StartInfo(string workingDirectory, IEnumerable<string> engineArgs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = Program,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string arg in FullArgs(engineArgs)) psi.ArgumentList.Add(arg);
        if (UnderWine)
        {
            if (!string.IsNullOrWhiteSpace(WinePrefix)) psi.Environment["WINEPREFIX"] = WinePrefix;
            // Wine's own chatter belongs in the log at most, never on the console.
            psi.Environment["WINEDEBUG"] = "-all";
        }
        return psi;
    }

    /// <summary>The whole command line, for --dry-run and the log.</summary>
    public string CommandLine(IEnumerable<string> engineArgs) =>
        Quote(Program) + " " + string.Join(' ', FullArgs(engineArgs).Select(Quote));

    /// <summary>One line for check: what starts the engine, and how.</summary>
    public string Describe()
    {
        if (!UnderWine) return Program + " (the game's own engine)";
        string runner = RunnerArgs.Count > 0 ? $"{Program} {string.Join(' ', RunnerArgs)}" : Program;
        return $"{runner} running {AsEnginePath(EngineProgram)} (a Windows program, through Wine)";
    }

    static string Quote(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

    static string FindOnPath(string name)
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // A malformed PATH entry isn't worth stopping for.
            }
        }
        return null;
    }

    static string ExpandHome(string value)
    {
        if (value == "~") return Platform.Home;
        if (value.StartsWith("~/", StringComparison.Ordinal)) return Path.Combine(Platform.Home, value.Substring(2));
        return value;
    }

    /// <summary>"proton run" and quoted paths split the way a shell would.</summary>
    public static List<string> SplitCommandLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, started = false;
        foreach (char c in line ?? "")
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }
            if (!quoted && char.IsWhiteSpace(c))
            {
                if (started)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
                continue;
            }
            current.Append(c);
            started = true;
        }
        if (started) parts.Add(current.ToString());
        return parts;
    }
}

/// <summary>What game build is installed, and whether it changed under a running host.</summary>
static class GameBuild
{
    public static string Version(DedicatedConfig cfg) => At(cfg.Game);

    /// <summary>The first line of a game folder's VERSION.txt, or empty when it has none.</summary>
    public static string At(string folder)
    {
        try
        {
            string file = Path.Combine(folder ?? "", "VERSION.txt");
            if (!File.Exists(file)) return "";
            return File.ReadAllLines(file).FirstOrDefault()?.Trim() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Changes when the game updates: the version line, and the engine file. A
    /// running host watches this and restarts its servers on the new build.
    /// </summary>
    public static string Stamp(DedicatedConfig cfg)
    {
        var sb = new StringBuilder(Version(cfg));
        foreach (string name in new[] { "bin/host-probe.exe", "bin/host-probe" })
        {
            string file = Path.Combine(cfg.Game ?? "", name);
            try
            {
                if (File.Exists(file))
                {
                    var info = new FileInfo(file);
                    sb.Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
                }
            }
            catch
            {
                // A locked file still gets watched by its version line.
            }
        }
        return sb.ToString();
    }
}
