namespace RenaissanceDedicated;

/// <summary>
/// `update` on the command line: what the release page says, and with --apply,
/// download it and put it in the game folder. This is the same code the running
/// host uses on its timer; it exists so an owner can update once, now, and see
/// exactly what happened instead of waiting for a check.
/// </summary>
static class UpdateCommand
{
    public static int Print(DedicatedConfig cfg, bool apply)
    {
        Console.WriteLine($"game       {cfg.Game}");
        var check = Updater.Check(cfg);

        Console.WriteLine("installed  " + (check.InstalledLine.Length > 0
            ? check.InstalledLine
            : "no VERSION.txt in the game folder"));
        if (!check.Ok)
        {
            Console.Error.WriteLine("error: " + check.Error);
            return 2;
        }

        var release = check.Release;
        Console.WriteLine($"latest     {release.Name}" +
                          (release.Tag.Length > 0 ? $"  ({release.Tag})" : "") +
                          (release.Size > 0 ? $"  {Megabytes(release.Size)}" : ""));

        if (!check.Newer)
        {
            Console.WriteLine("the game is up to date");
            return 0;
        }

        Console.WriteLine($"update available: {(check.Installed.Length > 0 ? check.Installed : "unknown")} -> {release.Version}");
        if (!apply)
        {
            Console.WriteLine("run `project-renaissance-dedicated update --apply` to install it now;");
            Console.WriteLine("a running host with auto_update = true does this on its own.");
            return 0;
        }

        var stage = Updater.Stage(cfg, line => Console.WriteLine(line));
        if (!stage.Ready)
        {
            Console.Error.WriteLine("error: " + stage.Error);
            return 2;
        }
        if (!Updater.Install(cfg, stage, line => Console.WriteLine(line), out string problem))
        {
            Console.Error.WriteLine("error: " + problem);
            return 2;
        }

        Console.WriteLine($"installed {GameBuild.Version(cfg)}");
        Console.WriteLine("start the servers to run the new build.");
        return 0;
    }

    static string Megabytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB"
        : $"{Math.Max(1, bytes / (1024 * 1024))} MB";
}
