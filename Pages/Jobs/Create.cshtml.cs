using System.ComponentModel.DataAnnotations;
using Marketplace.Data;
using Marketplace.Models;
using Marketplace.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Jobs;

// Only users with the Client role can open this page (admins and freelancers get "access denied").
[Authorize(Roles = Roles.Client)]
public class CreateModel : PageModel
{
    private readonly AppDbContext _db;
    public CreateModel(AppDbContext db) => _db = db;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, StringLength(120, MinimumLength = 5)]
        public string Title { get; set; } = "";

        [Required, StringLength(4000, MinimumLength = 20)]
        public string Description { get; set; } = "";

        [Range(1, 1000000)]
        public decimal Budget { get; set; }
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var job = new Job
        {
            ClientId = User.GetUserId(),   // always the logged-in user, never a form value
            Title = Input.Title.Trim(),
            Description = Input.Description.Trim(),
            Budget = Math.Round(Input.Budget, 2)
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return RedirectToPage("/Jobs/Details", new { id = job.Id });
    }
}
