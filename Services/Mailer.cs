using System.Net;
using System.Text;
using System.Text.Json;
using Marketplace.Data;
using Marketplace.Models;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Services;

/// <summary>
/// Sends short, branded event emails (signup, approval, hired, delivered, paid...) through the
/// same Google Apps Script mail sender the verification code uses (Email:ScriptUrl / Email:ScriptSecret).
/// Sending never blocks or breaks the page: it runs in the background and failures are only logged.
/// </summary>
public class Mailer
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly IConfiguration _config;
    private readonly ILogger<Mailer> _log;
    private readonly IServiceScopeFactory _scopes;

    public Mailer(IConfiguration config, ILogger<Mailer> log, IServiceScopeFactory scopes)
    {
        _config = config;
        _log = log;
        _scopes = scopes;
        SiteUrlStatic = (_config["Marketplace:SiteUrl"] ?? SiteUrlStatic).TrimEnd('/');
    }

    public string SiteUrl => (_config["Marketplace:SiteUrl"] ?? "https://nudge.fady-n-gwargeous.workers.dev").TrimEnd('/');

    /// <summary>Wraps a message in the Nudge look: royal-blue header, white card, one button.</summary>
    /// <summary>Absolute site address for images inside emails (set once at startup from config).</summary>
    public static string SiteUrlStatic { get; set; } = "https://nudge.fady-n-gwargeous.workers.dev";

    public static string Layout(string heading, string bodyHtml, string? buttonText = null, string? buttonUrl = null)
    {
        var sb = new StringBuilder();
        sb.Append("<div style='background:#f3f5fb;padding:24px 12px;font-family:Arial,Helvetica,sans-serif'>");
        sb.Append("<div style='max-width:520px;margin:0 auto;background:#ffffff;border-radius:14px;overflow:hidden;border:1px solid #e1e6f5'>");
        sb.Append($"<div style='background:#ffffff;padding:22px 24px 6px;text-align:center'><a href='{SiteUrlStatic}'><img src='{SiteUrlStatic}/brand/logo-email.jpg' alt='Nudge' width='150' style='border:0;display:inline-block;width:150px;height:auto'></a></div><div style='height:4px;background:#1d3fa8'></div>");
        sb.Append("<div style='padding:24px;color:#1b2340;font-size:15px;line-height:1.55'>");
        sb.Append($"<h2 style='margin:0 0 12px;color:#1d3fa8;font-size:20px'>{WebUtility.HtmlEncode(heading)}</h2>");
        sb.Append(bodyHtml);
        if (!string.IsNullOrEmpty(buttonText) && !string.IsNullOrEmpty(buttonUrl))
            sb.Append($"<p style='margin:22px 0 4px'><a href='{WebUtility.HtmlEncode(buttonUrl)}' style='background:#1d3fa8;color:#ffffff;text-decoration:none;padding:12px 22px;border-radius:10px;font-weight:bold;display:inline-block'>{WebUtility.HtmlEncode(buttonText)}</a></p>");
        sb.Append("</div>");
        sb.Append("<div style='padding:14px 24px;background:#f7f8fd;color:#7a829c;font-size:12px'>You get this email because of your activity on Nudge, the freelance marketplace for Egypt.</div>");
        sb.Append("</div></div>");
        return sb.ToString();
    }

    /// <summary>Encodes user text so names and titles cannot inject HTML into an email.</summary>
    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Sends a branded email without waiting for it. Never throws.</summary>
    public void Send(string? to, string subject, string heading, string bodyHtml, string? buttonText = null, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(to)) return;
        var url = path is null ? null : SiteUrl + path;
        var html = Layout(heading, bodyHtml, buttonText, url);
        var text = $"{heading}\n\n{System.Text.RegularExpressions.Regex.Replace(bodyHtml.Replace("</p>", "\n").Replace("<br>", "\n"), "<.*?>", "")}"
            + (url is null ? "" : $"\n\n{buttonText}: {url}");
        _ = Task.Run(() => PostAsync(to!, subject, text, html));
    }

    /// <summary>Emails every admin (the site owner).</summary>
    public void SendToAdmins(string subject, string heading, string bodyHtml, string? buttonText = null, string? path = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var admins = await db.Users.AsNoTracking().Where(u => u.Role == Roles.Admin && u.IsActive).Select(u => u.Email).ToListAsync();
                foreach (var a in admins) Send(a, subject, heading, bodyHtml, buttonText, path);
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not look up admins for an email."); }
        });
    }

    /// <summary>Tells the owner a new person verified their email and waits for approval.</summary>
    public void NotifyNewSignup(User u) =>
        SendToAdmins("New signup waiting for approval: " + u.FullName, "A new member wants to join",
            $"<p><strong>{E(u.FullName)}</strong> ({E(u.Role)}, {E(u.Email)}) verified their email and is waiting for your approval.</p>",
            "Review members", "/Admin/Users");

    /// <summary>Sends the verification email (same branded look).</summary>
    public Task<bool> SendNowAsync(string to, string subject, string heading, string bodyHtml, string text)
        => PostAsync(to, subject, text, Layout(heading, bodyHtml));

    private async Task<bool> PostAsync(string to, string subject, string text, string html)
    {
        var url = _config["Email:ScriptUrl"];
        var secret = _config["Email:ScriptSecret"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(secret))
        {
            _log.LogWarning("Email sender is not configured.");
            return false;
        }
        var payload = JsonSerializer.Serialize(new { secret, to, subject, text, html, name = "Nudge" });
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
