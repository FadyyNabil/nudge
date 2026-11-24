using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Marketplace.Models;

namespace Marketplace.Pages.Admin;

[Authorize(Roles = Roles.Admin)]
public class LogsModel : PageModel
{
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    public LogsModel(IConfiguration config, IWebHostEnvironment env) { _config = config; _env = env; }

    public List<string> Lines { get; private set; } = new();
    public int ErrorCount { get; private set; }
    public string FileName { get; private set; } = "";

    public void OnGet()
    {
        var path = _config["Logging:File:Path"] ?? "App_Data/logs";
        var dir = Path.IsPathRooted(path) ? path : Path.Combine(_env.ContentRootPath, path);
        if (!Directory.Exists(dir)) return;
        var file = Directory.GetFiles(dir, "app-*.log").OrderByDescending(f => f).FirstOrDefault();
        if (file is null) return;
        FileName = Path.GetFileName(file);
        string[] all;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var sr = new StreamReader(fs))
            all = sr.ReadToEnd().Split('\n');
        // Keep entries that start with a timestamp line and are warnings or worse, newest first.
        var entries = all.Where(l => l.Contains("[ERROR]") || l.Contains("[CRITICAL]") || l.Contains("[WARNING]")).ToList();
        ErrorCount = entries.Count(l => !l.Contains("[WARNING]"));
        Lines = entries.AsEnumerable().Reverse().Take(100).ToList();
    }
}
