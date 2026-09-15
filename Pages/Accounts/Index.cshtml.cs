using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaperTray.Models;

namespace PaperTray.Pages.Accounts;

public class IndexModel : PageModel
{
    private readonly Func<IDbConnection> _dbFactory;

    public List<Account> Accounts { get; private set; } = new();

    [BindProperty]
    [Required(ErrorMessage = "Enter an account name.")]
    [StringLength(100, ErrorMessage = "Account names must be 100 characters or fewer.")]
    public string NewAccountName { get; set; } = string.Empty;

    [BindProperty]
    public bool NewAccountIsOnBudget { get; set; } = true;

    public IndexModel(Func<IDbConnection> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task OnGetAsync()
    {
        await LoadAccountsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        NewAccountName = NewAccountName.Trim();
        if (!ModelState.IsValid)
        {
            await LoadAccountsAsync();
            return Page();
        }

        using var db = _dbFactory();
        int inserted = await db.ExecuteAsync(
            """
            INSERT INTO Accounts (Name, IsOnBudget)
            SELECT @Name, @IsOnBudget
            WHERE NOT EXISTS (
                SELECT 1
                FROM Accounts
                WHERE Name = @Name COLLATE NOCASE
            )
            """,
            new
            {
                Name = NewAccountName,
                IsOnBudget = NewAccountIsOnBudget,
            });

        if (inserted == 0)
        {
            ModelState.AddModelError(nameof(NewAccountName), "An account with this name already exists.");
            await LoadAccountsAsync();
            return Page();
        }

        return RedirectToPage();
    }

    private async Task LoadAccountsAsync()
    {
        using var db = _dbFactory();
        Accounts = (await db.QueryAsync<Account>(
            "SELECT Id, Name, IsOnBudget FROM Accounts ORDER BY Id")).AsList();
    }
}
