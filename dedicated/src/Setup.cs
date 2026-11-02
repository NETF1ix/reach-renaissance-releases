namespace RenaissanceDedicated;

/// <summary>
/// First run in an empty folder: build the same layout a hosting box needs, so a
/// downloaded .exe can just be dropped in a folder and opened. Nothing is
/// overwritten: every file here only appears when it is missing.
/// </summary>
static class Setup
{
    public static bool IsFirstRun() =>
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "dedicated.toml")) &&
        !File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "dedicated.toml"));

    public static int Run()
    {
        string root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool linux = !OperatingSystem.IsWindows();

        Console.WriteLine("Project Renaissance dedicated servers - first run");
        Console.WriteLine("folder: " + root);
        Console.WriteLine();

        foreach (string dir in new[]
                 {
                     "game", "content", Path.Combine("content", "Game Modes"),
                     Path.Combine("content", "Maps"), Path.Combine("content", "Mods"),
                     "playlists", "data", "logs",
                 })
            Directory.CreateDirectory(Path.Combine(root, dir));

        Extract("tpl/content/README.txt", Path.Combine(root, "content", "README.txt"));
        Extract("tpl/game/COPY GAME FILES HERE.txt", Path.Combine(root, "game", "COPY GAME FILES HERE.txt"));
        Extract("tpl/playlists/slayer.playlist.json", Path.Combine(root, "playlists", "slayer.playlist.json"));

        string configSource = linux ? "tpl/linux.toml" : "tpl/windows.toml";
        string configText = ReadResource(configSource);
        if (configText != null && linux)
        {
            // One folder, not a home-directory install: keep game/ next to the host.
            configText = System.Text.RegularExpressions.Regex.Replace(
                configText, @"(?m)^game\s*=.*$", "game = \"game\"");
        }
        WriteIfMissing(Path.Combine(root, "dedicated.toml"), configText);

        if (linux)
            Extract("tpl/linux-README.txt", Path.Combine(root, "README-linux.txt"));

        string panel = CopyDesktopApp(root, linux);

        WriteIfMissing(Path.Combine(root, "START-HERE.txt"), StartHere(linux, panel));

        Console.WriteLine("created:");
        Console.WriteLine("  dedicated.toml          every server, port and the RCON password");
        Console.WriteLine("  game\\                   the Reach Renaissance game files go here");
        Console.WriteLine("  content\\                maps, game types and mods every server shares");
        Console.WriteLine("  playlists\\              slayer.playlist.json to start from");
        Console.WriteLine("  data\\, logs\\            written while servers run");
        if (panel != null)
            Console.WriteLine("  panel" + Path.DirectorySeparatorChar + panel + "    the OmniVault control room (RCON)");
        Console.WriteLine();
        Console.WriteLine("next:");
        Console.WriteLine("  1. put the game's files in game\\ (see game\\COPY GAME FILES HERE.txt)");
        Console.WriteLine("  2. set a long [rcon] password in dedicated.toml");
        Console.WriteLine("  3. run this program again with: check");
        Console.WriteLine("  4. then run it with no arguments to start the servers");
        Console.WriteLine();
        return 0;
    }

    const string PanelResource = "tpl/panel/OmniVault-Setup.exe";

    /// <summary>
    /// The OmniVault panel ships inside this program, so one downloaded file builds the
    /// whole folder, panel included. A copy sitting next to the program is used when the
    /// embedded one is absent.
    /// </summary>
    static string CopyDesktopApp(string root, bool linux)
    {
        if (linux) return null;   // the panel is a Windows program

        try
        {
            string panelDir = Path.Combine(root, "panel");
            string name = Path.GetFileName(PanelResource);
            using (Stream embedded = typeof(Setup).Assembly.GetManifestResourceStream(PanelResource))
            {
                if (embedded != null)
                {
                    Directory.CreateDirectory(panelDir);
                    string target = Path.Combine(panelDir, name);
                    if (!File.Exists(target))
                        using (FileStream file = File.Create(target))
                            embedded.CopyTo(file);
                    return name;
                }
            }

            foreach (string file in Directory.GetFiles(root, "OmniVault*.exe"))
            {
                string adjacent = Path.GetFileName(file);
                if (adjacent.Equals("OmniVaultAgent.exe", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(panelDir);
                string target = Path.Combine(panelDir, adjacent);
                if (!File.Exists(target)) File.Copy(file, target);
                return adjacent;
            }
        }
        catch
        {
            // A missing or unreadable desktop app is not a setup failure.
        }
        return null;
    }

    static void Extract(string resource, string path)
    {
        string text = ReadResource(resource);
        if (text != null) WriteIfMissing(path, text);
    }

    static void WriteIfMissing(string path, string text)
    {
        if (text == null || File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    static string ReadResource(string name)
    {
        using Stream stream = typeof(Setup).Assembly.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string StartHere(bool linux, string panel) => $@"Project Renaissance dedicated servers
=====================================

This folder was built the first time you opened the program in it.

  game\        the Reach Renaissance game files. See game\COPY GAME FILES HERE.txt.
  content\     maps, game types and mods that every server below can use.
  playlists\   slayer.playlist.json is a working rotation to start from.
  data\ logs\  written by the servers while they run.

Edit dedicated.toml to set your server names, ports and a long [rcon] password,
then run:

  {(linux ? "./project-renaissance-dedicated" : "project-renaissance-dedicated.exe")} check
  {(linux ? "./project-renaissance-dedicated" : "project-renaissance-dedicated.exe")}

{(linux
    ? "The OmniVault control room runs on Windows, so this download doesn't include it."
    : panel != null
        ? $@"The OmniVault control room is in panel\{panel}. Install it to run the servers
from a browser (RCON), and add each server there with its RCON port (the same
number as its game port unless dedicated.toml sets rcon_port)."
        : @"Put the OmniVault desktop app next to this program before the first run and
it is copied into panel\ automatically.")}
";
}
