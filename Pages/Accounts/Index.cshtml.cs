using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaperTray.Models;
using PaperTray.Services;
using StarFederation.Datastar;
using StarFederation.Datastar.DependencyInjection;

namespace PaperTray.Pages.Accounts;

[IgnoreAntiforgeryToken]
public class IndexModel : PageModel
{
    private readonly Func<IDbConnection> _dbFactory;
    private readonly IDatastarService _datastar;
    private readonly IViewRenderService _viewRenderService;

    public List<Account> Accounts { get; private set; } = new();

    [BindProperty]
    [Required(ErrorMessage = "Enter an account name.")]
    [StringLength(100, ErrorMessage = "Account names must be 100 characters or fewer.")]
    public string NewAccountName { get; set; } = string.Empty;

    [BindProperty]
    public bool NewAccountIsOnBudget { get; set; } = true;

    [BindProperty]
    public AccountInput EditInput { get; set; } = new();

    public bool IsEditing => EditInput.Id.HasValue;

    public IndexModel(
        Func<IDbConnection> dbFactory,
        IDatastarService datastar,
        IViewRenderService viewRenderService)
    {
        _dbFactory = dbFactory;
        _datastar = datastar;
        _viewRenderService = viewRenderService;
    }

    public async Task<IActionResult> OnGetAsync(int? editId = null)
    {
        await LoadAccountsAsync();

        if (editId.HasValue)
        {
            using var db = _dbFactory();
            var account = await db.QuerySingleOrDefaultAsync<AccountInput>(
                """
                SELECT Id, Name, IsOnBudget
                FROM Accounts
                WHERE Id = @Id
                """,
                new { Id = editId.Value });

            if (account is null)
            {
                return NotFound();
            }

            EditInput = account;
        }

        return Page();
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

    public async Task<IActionResult> OnPostSaveAsync()
    {
        EditSignals? signals = await _datastar.ReadSignalsAsync<EditSignals>();
        EditInput = new AccountInput
        {
            Id = signals?.EditId,
            Name = signals?.EditName?.Trim() ?? string.Empty,
            IsOnBudget = signals?.EditIsOnBudget ?? true,
        };
        TryValidateModel(EditInput, nameof(EditInput));

        if (!ModelState.IsValid)
        {
            await LoadAccountsAsync();
            await PatchContentAsync();
            return new EmptyResult();
        }

        using var db = _dbFactory();
        int updated = await db.ExecuteAsync(
            """
            UPDATE Accounts
            SET Name = @Name,
                IsOnBudget = @IsOnBudget
            WHERE Id = @Id
              AND NOT EXISTS (
                  SELECT 1
                  FROM Accounts
                  WHERE Name = @Name COLLATE NOCASE
                    AND Id <> @Id
              )
            """,
            new
            {
                EditInput.Id,
                EditInput.Name,
                EditInput.IsOnBudget,
            });

        if (updated == 0)
        {
            bool accountExists = await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Accounts WHERE Id = @Id)",
                new { EditInput.Id });
            if (!accountExists)
            {
                return NotFound();
            }

            ModelState.AddModelError(nameof(EditInput.Name), "An account with this name already exists.");
            await LoadAccountsAsync();
            await PatchContentAsync();
            return new EmptyResult();
        }

        await LoadAccountsAsync();
        EditInput = new AccountInput();
        await PatchContentAsync();

        return new EmptyResult();
    }

    private async Task PatchContentAsync()
    {
        string content = await _viewRenderService.RenderPartialToStringAsync(
            "/Pages/Accounts/_AccountContent.cshtml",
            this);
        await _datastar.PatchElementsAsync(
            content,
            new PatchElementsOptions
            {
                Selector = "#accounts-content",
                PatchMode = ElementPatchMode.Replace,
            });
    }

    private async Task LoadAccountsAsync()
    {
        using var db = _dbFactory();
        Accounts = (await db.QueryAsync<Account>(
            "SELECT Id, Name, IsOnBudget FROM Accounts ORDER BY Id")).AsList();
    }

    public sealed class AccountInput
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Enter an account name.")]
        [StringLength(100, ErrorMessage = "Account names must be 100 characters or fewer.")]
        public string Name { get; set; } = string.Empty;

        public bool IsOnBudget { get; set; } = true;
    }

    public sealed class EditSignals
    {
        public int? EditId { get; set; }
        public string? EditName { get; set; }
        public bool EditIsOnBudget { get; set; }
    }
}
