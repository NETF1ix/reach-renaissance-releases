using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RenaissanceDedicated;

/// <summary>
/// The dedicated server's own admin dashboard: a password-protected HTTP API on
/// 127.0.0.1 (host-probe's server_admin.rs). This host uses it for the one thing
/// the engine can't be asked any other way — ending a game that a player joined
/// after it started, because Reach Renaissance can't spawn a player into a game
/// already in progress.
/// </summary>
static class AdminApi
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(4) };
    static string _passwordPath;
    static string _password;
    static long _passwordTicks;

    public sealed class Reply
    {
        public bool Ok;
        public string Message;
    }

    /// <summary>The dashboard password the engine wrote into the server's admin folder.</summary>
    public static string Password(string serverDir)
    {
        string file = Path.Combine(serverDir, "admin", "password.txt");
        try
        {
            long ticks = File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0;
            if (file == _passwordPath && ticks == _passwordTicks) return _password;
            _passwordPath = file;
            _passwordTicks = ticks;
            _password = File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        }
        catch
        {
            _password = "";
        }
        return _password;
    }

    /// <summary>Sends one command, such as {"action":"end_game"}. Never throws.</summary>
    public static Reply Send(int adminPort, string password, object command)
    {
        if (string.IsNullOrEmpty(password))
            return new Reply { Ok = false, Message = "the engine hasn't written its dashboard password yet" };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{adminPort}/api/command")
            {
                Content = new StringContent(JsonSerializer.Serialize(command), Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + password);

            using var response = Client.SendAsync(request).GetAwaiter().GetResult();
            string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            string message = null;
            bool ok = response.IsSuccessStatusCode;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString();
                if (doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.False)
                    ok = false;
            }
            catch
            {
                // Not JSON: the status line is the best we have.
            }
            if (!ok)
                return new Reply { Ok = false, Message = message ?? $"the dashboard answered {(int)response.StatusCode}" };
            return new Reply { Ok = true, Message = message ?? "done" };
        }
        catch (Exception e)
        {
            return new Reply { Ok = false, Message = "the dashboard didn't answer: " + Innermost(e).Message };
        }
    }

    /// <summary>A read from the engine's dashboard: /api/status, /api/bans, /api/log, /api/audit.</summary>
    public static (bool Ok, string Text, string Problem) Get(int adminPort, string password, string path)
    {
        if (string.IsNullOrEmpty(password))
            return (false, null, "the engine hasn't written its dashboard password yet");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{adminPort}{path}");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + password);
            using var response = Client.SendAsync(request).GetAwaiter().GetResult();
            string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return (false, text, $"the dashboard answered {(int)response.StatusCode}");
            return (true, text, null);
        }
        catch (Exception e)
        {
            return (false, null, "the dashboard didn't answer: " + Innermost(e).Message);
        }
    }

    /// <summary>The engine's live view of one server, straight from its dashboard.</summary>
    public static string Status(int adminPort, string password) =>
        Get(adminPort, password, "/api/status").Text;

    static Exception Innermost(Exception e)
    {
        while (e.InnerException != null) e = e.InnerException;
        return e;
    }
}
