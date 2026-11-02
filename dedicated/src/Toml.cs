using System.Globalization;
using System.Text;

namespace RenaissanceDedicated;

/// <summary>Raised for anything wrong in a config file, with the file and line.</summary>
sealed class ConfigError : Exception
{
    public string File { get; }
    public int Line { get; }

    public ConfigError(string file, int line, string message) : base(message)
    {
        File = file;
        Line = line;
    }
}

/// <summary>
/// One TOML table: the keys it holds, plus the line each key was written on so
/// mistakes in a hand-edited dedicated.toml can be reported by line.
/// </summary>
sealed class TomlTable
{
    public readonly Dictionary<string, object> Values = new(StringComparer.Ordinal);
    public readonly Dictionary<string, int> KeyLines = new(StringComparer.Ordinal);
    public int Line;

    public void Set(string key, object value, int line)
    {
        Values[key] = value;
        KeyLines[key] = line;
    }
}

/// <summary>
/// A small TOML reader covering what a dedicated.toml uses: comments, bare keys,
/// basic and literal strings, integers, floats, booleans, arrays of those, [tables]
/// and [[arrays of tables]]. Anything else is refused with its line number rather
/// than guessed at, so a typo can't silently change what the servers do.
/// </summary>
static class Toml
{
    public static TomlTable ParseFile(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e)
        {
            throw new ConfigError(path, 0, $"can't read {Path.GetFileName(path)}: {e.Message}");
        }
        return Parse(text, path);
    }

    public static TomlTable Parse(string text, string path)
    {
        var root = new TomlTable();
        TomlTable tables = root;   // [table] that plain keys go into
        TomlTable current = root;  // [table] or the newest [[table]] element
        int i = 0, line = 1;

        while (i < text.Length)
        {
            SkipSpaceAndComments(text, ref i, ref line);
            if (i >= text.Length) break;

            if (text[i] == '[')
            {
                bool array = i + 1 < text.Length && text[i + 1] == '[';
                int headerLine = line;
                i += array ? 2 : 1;
                var name = new StringBuilder();
                while (i < text.Length && text[i] != ']' && text[i] != '\n')
                {
                    name.Append(text[i]);
                    i++;
                }
                if (i >= text.Length || text[i] != ']')
                    throw new ConfigError(path, headerLine, "unclosed table header");
                i++;
                if (array)
                {
                    if (i >= text.Length || text[i] != ']')
                        throw new ConfigError(path, headerLine, "unclosed [[table]] header");
                    i++;
                }
                string key = name.ToString().Trim();
                if (key.Length == 0)
                    throw new ConfigError(path, headerLine, "empty table name");
                if (key.Contains('.'))
                    throw new ConfigError(path, headerLine, $"nested tables aren't supported: [{key}]");

                if (array)
                {
                    if (!tables.Values.TryGetValue(key, out var existing))
                    {
                        var list = new List<TomlTable>();
                        tables.Set(key, list, headerLine);
                        existing = list;
                    }
                    if (existing is not List<TomlTable> items)
                        throw new ConfigError(path, headerLine, $"[[{key}]] and {key} can't both be set");
                    var element = new TomlTable { Line = headerLine };
                    items.Add(element);
                    current = element;
                }
                else
                {
                    if (!tables.Values.TryGetValue(key, out var existing))
                    {
                        var table = new TomlTable { Line = headerLine };
                        tables.Set(key, table, headerLine);
                        existing = table;
                    }
                    if (existing is not TomlTable table2)
                        throw new ConfigError(path, headerLine, $"[{key}] and [[{key}]] can't both be set");
                    current = table2;
                }
                continue;
            }

            int keyLine = line;
            int keyStart = i;
            while (i < text.Length && text[i] != '=' && text[i] != '\n')
                i++;
            if (i >= text.Length || text[i] != '=')
                throw new ConfigError(path, keyLine, $"expected key = value, found \"{Line(text, keyStart, i)}\"");
            string keyName = text.Substring(keyStart, i - keyStart).Trim();
            i++; // '='
            if (keyName.Length == 0)
                throw new ConfigError(path, keyLine, "empty key");
            if (keyName.Contains('.'))
                throw new ConfigError(path, keyLine, $"dotted keys aren't supported: {keyName}");
            if (keyName.Contains('"') || keyName.Contains('\''))
                throw new ConfigError(path, keyLine, $"quoted keys aren't supported: {keyName}");

            SkipSpaceAndComments(text, ref i, ref line);
            object value = ParseValue(text, path, ref i, ref line, keyLine);
            if (current.Values.ContainsKey(keyName))
                throw new ConfigError(path, keyLine, $"{keyName} is set twice in this block");
            current.Set(keyName, value, keyLine);
        }

        return root;
    }

    static object ParseValue(string text, string path, ref int i, ref int line, int startLine)
    {
        if (i >= text.Length)
            throw new ConfigError(path, startLine, "missing value");
        char c = text[i];

        if (c == '"')
        {
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= text.Length || text[i] == '\n')
                    throw new ConfigError(path, startLine, "unclosed string");
                c = text[i++];
                if (c == '"') break;
                if (c == '\\' && i < text.Length)
                {
                    char e = text[i++];
                    sb.Append(e switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => throw new ConfigError(path, startLine, $"unknown escape \\{e}"),
                    });
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        if (c == '\'')
        {
            i++;
            int start = i;
            while (i < text.Length && text[i] != '\'' && text[i] != '\n')
                i++;
            if (i >= text.Length || text[i] != '\'')
                throw new ConfigError(path, startLine, "unclosed string");
            string literal = text.Substring(start, i - start);
            i++;
            return literal;
        }

        if (c == '[')
        {
            i++;
            var items = new List<object>();
            while (true)
            {
                SkipSpaceAndComments(text, ref i, ref line);
                if (i >= text.Length)
                    throw new ConfigError(path, startLine, "unclosed array");
                if (text[i] == ']')
                {
                    i++;
                    return items;
                }
                items.Add(ParseValue(text, path, ref i, ref line, line));
                SkipSpaceAndComments(text, ref i, ref line);
                if (i < text.Length && text[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < text.Length && text[i] == ']')
                {
                    i++;
                    return items;
                }
                throw new ConfigError(path, line, "expected , or ] in array");
            }
        }

        // A bare value: read up to the end of the line, a comma or a bracket.
        int valueStart = i;
        while (i < text.Length && text[i] != '\n' && text[i] != ',' && text[i] != ']' && text[i] != '#')
            i++;
        string token = text.Substring(valueStart, i - valueStart).Trim();
        if (token.Length == 0)
            throw new ConfigError(path, startLine, "missing value");
        if (token == "true") return true;
        if (token == "false") return false;
        string number = token.Replace("_", "");
        if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
            return l;
        if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            return d;
        if (token.StartsWith("{") || token.StartsWith("["))
            throw new ConfigError(path, startLine, $"inline tables and nested arrays aren't supported: {token}");
        throw new ConfigError(path, startLine, $"can't read the value \"{token}\" (quote it to mean text)");
    }

    static void SkipSpaceAndComments(string text, ref int i, ref int line)
    {
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                line++;
                i++;
            }
            else if (c == '\r' || c == ' ' || c == '\t')
            {
                i++;
            }
            else if (c == '#')
            {
                while (i < text.Length && text[i] != '\n')
                    i++;
            }
            else
            {
                return;
            }
        }
    }

    static string Line(string text, int from, int to)
    {
        int end = to;
        while (end > from && (text[end - 1] == ' ' || text[end - 1] == '\t' || text[end - 1] == '\r'))
            end--;
        return text.Substring(from, end - from).Trim();
    }

    // ---- typed readers, all reporting the file and line on failure ----

    public static bool Has(TomlTable t, string key) => t.Values.ContainsKey(key);

    public static string Str(TomlTable t, string key, string file, string fallback = null)
    {
        if (!t.Values.TryGetValue(key, out var v)) return fallback;
        if (v is string s) return s;
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be text in quotes");
    }

    public static bool Bool(TomlTable t, string key, string file, bool fallback)
    {
        if (!t.Values.TryGetValue(key, out var v)) return fallback;
        if (v is bool b) return b;
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be true or false");
    }

    public static long Int(TomlTable t, string key, string file, long fallback)
    {
        if (!t.Values.TryGetValue(key, out var v)) return fallback;
        if (v is long l) return l;
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be a whole number");
    }

    public static List<string> StrArray(TomlTable t, string key, string file)
    {
        if (!t.Values.TryGetValue(key, out var v)) return new List<string>();
        if (v is List<object> list)
        {
            var outList = new List<string>();
            foreach (var item in list)
            {
                if (item is string s) outList.Add(s);
                else throw new ConfigError(file, t.KeyLines[key], $"{key} must be a list of quoted names");
            }
            return outList;
        }
        if (v is string single) return new List<string> { single };
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be a list of quoted names");
    }

    public static TomlTable Table(TomlTable t, string key, string file)
    {
        if (!t.Values.TryGetValue(key, out var v)) return null;
        if (v is TomlTable table) return table;
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be a [table]");
    }

    public static List<TomlTable> TableArray(TomlTable t, string key, string file)
    {
        if (!t.Values.TryGetValue(key, out var v)) return new List<TomlTable>();
        if (v is List<TomlTable> list) return list;
        throw new ConfigError(file, t.KeyLines[key], $"{key} must be written as [[{key}]] blocks");
    }

    public static int LineOf(TomlTable t, string key) => t.KeyLines.TryGetValue(key, out int line) ? line : t.Line;
}
