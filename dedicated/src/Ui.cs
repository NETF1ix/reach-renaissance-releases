using System.Runtime.InteropServices;
using System.Text;

namespace RenaissanceDedicated;

/// <summary>
/// The console this host shows while it runs: one line per server that stays up
/// to date in place, and a line whenever something happens — a player joins, the
/// game changes, a server restarts, something goes wrong. The engine's own output
/// goes to its log (with --verbose it also shows here), so a server can run for
/// hours without filling the window with the same line twice.
/// </summary>
static class Ui
{
    static bool _live, _verbose;
    static readonly List<string> Block = new();
    static readonly object Gate = new();
    static string _blockText = "";
    static int _blockLines;
    static DateTime _lastRedirected = DateTime.MinValue;

    public static bool Verbose => _verbose;

    public static void Begin(bool verbose)
    {
        _verbose = verbose;
        try
        {
            _live = !Console.IsOutputRedirected && EnableAnsi() && Console.WindowWidth > 20;
        }
        catch
        {
            _live = false;
        }
        if (_live)
        {
            try
            {
                Console.Title = "Project Renaissance dedicated servers";
                Console.Write("\x1b[?25l");   // the cursor only gets in the block's way
            }
            catch
            {
                // A console that won't take escapes or a title is still a console.
            }
        }
    }

    public static void End()
    {
        lock (Gate)
        {
            if (_live)
            {
                ClearBlock();
                try
                {
                    Console.Write("\x1b[?25h");
                }
                catch
                {
                    // Leaving the cursor hidden is a small thing to lose.
                }
            }
            Block.Clear();
            _live = false;
        }
    }

    /// <summary>A line that stays: startup facts, joins, game changes, trouble.</summary>
    public static void Line(string text)
    {
        lock (Gate)
        {
            if (_live) ClearBlock();
            try
            {
                Console.Out.WriteLine(text);
            }
            catch
            {
                // A closed console must not take the servers down.
                return;
            }
            Redraw();
        }
    }

    public static void Event(string server, string text) =>
        Line($"{DateTime.Now:HH:mm:ss}  [{server}] {text}");

    /// <summary>The engine's own output, when --verbose asked for it.</summary>
    public static void VerboseLine(string server, string text)
    {
        if (_verbose) Line($"{DateTime.Now:HH:mm:ss}  [{server}] {text}");
    }

    /// <summary>The lines redrawn in place, one per server and a footer.</summary>
    public static void Show(IEnumerable<string> lines)
    {
        var list = lines.ToList();
        string text = string.Join('\n', list);
        lock (Gate)
        {
            if (_live)
            {
                Block.Clear();
                Block.AddRange(list);
                if (text == _blockText) return;   // nothing changed: leave the screen alone
                _blockText = text;
                ClearBlock();
                Redraw();
                return;
            }

            // Output to a file or a service: only a change deserves a line, and
            // never more often than every few seconds.
            if (text == _blockText) return;
            if ((DateTime.UtcNow - _lastRedirected).TotalSeconds < 5) return;
            _blockText = text;
            _lastRedirected = DateTime.UtcNow;
            foreach (string line in list)
                Console.Out.WriteLine(line);
        }
    }

    static void Redraw()
    {
        if (!_live || Block.Count == 0) return;
        int width = Width();
        var sb = new StringBuilder();
        foreach (string line in Block)
            sb.Append("\x1b[2K").Append(Trim(line, width)).Append('\n');
        try
        {
            Console.Out.Write(sb.ToString());
            _blockLines = Block.Count;
        }
        catch
        {
            _live = false;
        }
    }

    static void ClearBlock()
    {
        if (!_live || _blockLines == 0) return;
        var sb = new StringBuilder("\r");
        sb.Append("\x1b[").Append(_blockLines).Append('A');
        for (int i = 0; i < _blockLines; i++)
        {
            sb.Append("\x1b[2K");
            if (i < _blockLines - 1) sb.Append("\x1b[1B");
        }
        sb.Append('\r');
        if (_blockLines > 1) sb.Append("\x1b[").Append(_blockLines - 1).Append('A');
        try
        {
            Console.Out.Write(sb.ToString());
        }
        catch
        {
            // Same: the next redraw finds out.
        }
        _blockLines = 0;
    }

    static int Width()
    {
        try
        {
            int width = Console.WindowWidth;
            return width > 20 ? width : 120;
        }
        catch
        {
            return 120;
        }
    }

    static string Trim(string text, int width)
    {
        if (text.Length <= width - 1) return text;
        return text.Substring(0, Math.Max(1, width - 4)) + "...";
    }

    // ---- Windows: the console needs to be told to read escape codes ----------

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetConsoleMode(IntPtr handle, uint mode);

    const int StdOutputHandle = -11;
    const uint EnableVirtualTerminalProcessing = 0x0004;

    static bool EnableAnsi()
    {
        if (!Platform.Windows) return true;   // Linux terminals read them as they are
        try
        {
            IntPtr handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
            if (!GetConsoleMode(handle, out uint mode)) return false;
            if ((mode & EnableVirtualTerminalProcessing) != 0) return true;
            return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
        catch
        {
            return false;
        }
    }
}
