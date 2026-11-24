using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Marketplace.Pages.Account;

public class LogoutModel : PageModel
{
    // GET just bounces home; logging out needs a POST (form button) so a random
    // link on another site cannot log you out.
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync("AuthCookie");
        return RedirectToPage("/Index");
    }
}
