using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    private readonly ILogger<ErrorModel> _log;
    public ErrorModel(ILogger<ErrorModel> log) => _log = log;

    public int Code { get; private set; } = 500;
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public string? RequestId { get; private set; }

    public void OnGet(int? code) => Fill(code);
    public void OnPost(int? code) => Fill(code);

    private void Fill(int? code)
    {
        var ex = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        Code = ex?.Error is not null ? 500 : (code ?? 500);
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        if (ex?.Error is not null)
            _log.LogError(ex.Error, "Unhandled error {RequestId} on {Method} {Path}", RequestId, Request.Method, ex.Path);
        (Title, Message) = Code switch
        {
            404 => ("This page could not be found", "The link may be old or mistyped. Let's get you back on track."),
            403 => ("Access denied", "Your account does not have permission to open that page."),
            400 => ("That request did not work", "Something in the request was not valid. Go back and try again."),
            401 => ("Please sign in", "You need to log in to open that page."),
            _ => ("Something went wrong on our side", "It is not you. The error was recorded and we will look into it. Try again in a minute."),
        };
        Response.StatusCode = Code;
        if (Code != 500) RequestId = null;
    }
}
