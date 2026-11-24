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
    private readonly Mailer _mailer;

    public EmailVerifier(IConfiguration config, ILogger<EmailVerifier> log, Mailer mailer)
    {
        _config = config;
        _log = log;
        _mailer = mailer;
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

    public Task<bool> SendCodeAsync(User u)
    {
        var code = MakeCode(u, Window(DateTime.UtcNow));
        var body = "<p>Your Nudge verification code is:</p>"
            + $"<p style='font-size:34px;letter-spacing:8px;font-weight:bold;color:#1d3fa8;margin:8px 0'>{code}</p>"
            + "<p style='color:#666'>It expires in 15 minutes. If you did not sign up, you can ignore this email.</p>";
        var text = $"Your Nudge verification code is {code}. It expires in 15 minutes. If you did not sign up, ignore this email.";
        return _mailer.SendNowAsync(u.Email, "Your Nudge verification code", "Verify your email", body, text);
    }
}
