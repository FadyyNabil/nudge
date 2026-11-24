using Marketplace.Data;
using Marketplace.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Pages.Admin;

[Authorize(Roles = Roles.Admin)]
public class TransactionsModel : PageModel
{
    private readonly AppDbContext _db;
    public TransactionsModel(AppDbContext db) => _db = db;

    public List<Job> Rows { get; private set; } = new();
    public decimal Earned { get; private set; }
    public decimal Pending { get; private set; }
    public decimal HeldNow { get; private set; }

    public async Task OnGetAsync()
    {
        Rows = await _db.Jobs.Include(j => j.Client).AsNoTracking()
            .Where(j => j.Status == JobStatus.InProgress || j.Status == JobStatus.Completed)
            .OrderByDescending(j => j.Id).ToListAsync();
        Earned = Rows.Where(j => j.Status == JobStatus.Completed).Sum(j => j.CommissionAmount ?? 0);
        Pending = Rows.Where(j => j.Status == JobStatus.InProgress).Sum(j => j.CommissionAmount ?? 0);
        // Client payments currently sitting in the demo escrow, waiting for the work to finish.
        HeldNow = Rows.Where(j => j.Escrow == EscrowStatus.Held).Sum(j => j.AgreedPrice ?? 0);
    }
}
