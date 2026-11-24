using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Models;

namespace Marketplace.Services;

/// <summary>
/// Makes and checks the 6-digit email verification codes, and sends them through a small
/// Google Apps Script web app (settings Email:ScriptUrl and Email:ScriptSecret).
/// Codes are not stored: a code is an HMAC of the user (keyed with the user's own password
/// hash) and a 15-minute time window, so it cannot be guessed without the hash.
/// </summary>
public class EmailVerifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly IConfiguration _config;
    private readonly ILogger<EmailVerifier> _log;

    public EmailVerifier(IConfiguration config, ILogger<EmailVerifier> log)
    {
        _config = config;
        _log = log;
    }

    private static long Window(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds() / 900;

    private static string MakeCode(User u, long window)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(u.PasswordHash));
        var b = h.ComputeHash(Encoding.UTF8.GetBytes($"{u.Id}|{u.Email}|{window}"));
        return (BitConverter.ToUInt32(b, 0) % 1000000u).ToString("D6");
    }

    /// <summary>True if the code is right and not older than 15 to 30 minutes.</summary>
    public bool Check(User u, string code)
    {
        code = (code ?? "").Trim();
        var now = Window(DateTime.UtcNow);
        var ok = false;
        for (var i = 0; i < 2; i++)
        {
            var expected = Encoding.UTF8.GetBytes(MakeCode(u, now - i));
            var given = Encoding.UTF8.GetBytes(code);
            if (CryptographicOperations.FixedTimeEquals(expected, given)) ok = true;
        }
        return ok;
    }

    public async Task<bool> SendCodeAsync(User u)
    {
        var url = _config["Email:ScriptUrl"];
        var secret = _config["Email:ScriptSecret"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
        {
            _log.LogWarning("Email sender is not configured.");
            return false;
        }
        var code = MakeCode(u, Window(DateTime.UtcNow));
        var payload = JsonSerializer.Serialize(new
        {
            secret,
            to = u.Email,
            subject = "Your Sho8lana verification code",
            text = $"Your Sho8lana verification code is {code}. It expires in 15 minutes. If you did not sign up, ignore this email.",
            html = $"<div style='font-family:Arial,sans-serif;max-width:420px'><h2 style='color:#1d3fa8'>Verify your email</h2><p>Your Sho8lana verification code is:</p><p style='font-size:32px;letter-spacing:6px;font-weight:bold'>{code}</p><p style='color:#666'>It expires in 15 minutes. If you did not sign up, ignore this email.</p></div>"
        });
        try
        {
            using var res = await Http.PostAsync(url, new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = (await res.Content.ReadAsStringAsync()).Trim();
            if (res.IsSuccessStatusCode && body == "ok") return true;
            _log.LogWarning("Email sender answered {Status}: {Body}", (int)res.StatusCode, body.Length > 80 ? body[..80] : body);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Email sender failed.");
            return false;
        }
    }
}
