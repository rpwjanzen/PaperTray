using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaperTray.Models;

namespace PaperTray.Pages.Budget;

public class IndexModel : PageModel
{
    private readonly Func<IDbConnection> _dbFactory;

    public List<Envelope> Envelopes { get; private set; } = new();
    public decimal UnallocatedCash { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter an envelope name.")]
    [StringLength(100, ErrorMessage = "Envelope names must be 100 characters or fewer.")]
    public string NewEnvelopeName { get; set; } = string.Empty;

    public IndexModel(Func<IDbConnection> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task OnGetAsync()
    {
        await LoadBudgetAsync();
    }

    public async Task<IActionResult> OnPostCreateEnvelopeAsync()
    {
        NewEnvelopeName = NewEnvelopeName.Trim();
        if (string.IsNullOrWhiteSpace(NewEnvelopeName))
        {
            ModelState.AddModelError(nameof(NewEnvelopeName), "Enter an envelope name.");
        }
        else if (NewEnvelopeName.Length > 100)
        {
            ModelState.AddModelError(
                nameof(NewEnvelopeName),
                "Envelope names must be 100 characters or fewer.");
        }

        if (!ModelState.IsValid)
        {
            await LoadBudgetAsync();
            return Page();
        }

        using var db = _dbFactory();
        int inserted = await db.ExecuteAsync(
            """
            INSERT INTO Envelopes (CategoryName, AllocatedAmount, SpentAmount)
            SELECT @CategoryName, 0, 0
            WHERE NOT EXISTS (
                SELECT 1
                FROM Envelopes
                WHERE CategoryName = @CategoryName COLLATE NOCASE
            )
            """,
            new { CategoryName = NewEnvelopeName });

        if (inserted == 0)
        {
            ModelState.AddModelError(
                nameof(NewEnvelopeName),
                "An envelope with this name already exists.");
            await LoadBudgetAsync();
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameEnvelopeAsync(int id, string? name)
    {
        string newName = name?.Trim() ?? string.Empty;
        string? validationError = null;

        if (string.IsNullOrWhiteSpace(newName))
        {
            validationError = "Enter an envelope name.";
        }
        else if (newName.Length > 100)
        {
            validationError = "Envelope names must be 100 characters or fewer.";
        }

        using var db = _dbFactory();
        bool envelopeExists = await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM Envelopes WHERE Id = @Id)",
            new { Id = id });
        if (!envelopeExists)
        {
            return NotFound();
        }

        if (validationError is null)
        {
            bool duplicateName = await db.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM Envelopes
                    WHERE Id <> @Id
                      AND CategoryName = @Name COLLATE NOCASE
                )
                """,
                new { Id = id, Name = newName });
            if (duplicateName)
            {
                validationError = "An envelope with this name already exists.";
            }
        }

        if (validationError is not null)
        {
            await LoadBudgetAsync();
            Envelopes.Single(envelope => envelope.Id == id).RenameError = validationError;
            return Page();
        }

        await db.ExecuteAsync(
            "UPDATE Envelopes SET CategoryName = @Name WHERE Id = @Id",
            new { Name = newName, Id = id });

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAllocationAsync(int id, decimal allocated)
    {
        if (allocated < 0)
        {
            ModelState.AddModelError("allocated", "Allocation cannot be negative.");
            await LoadBudgetAsync();
            return Page();
        }

        using var db = _dbFactory();
        int updated = await db.ExecuteAsync(
            "UPDATE Envelopes SET AllocatedAmount = @Allocated WHERE Id = @Id",
            new { Allocated = allocated, Id = id });

        return updated == 0 ? NotFound() : RedirectToPage();
    }

    private async Task LoadBudgetAsync()
    {
        using var db = _dbFactory();
        Envelopes = (await db.QueryAsync<Envelope>(
            "SELECT * FROM Envelopes ORDER BY Id")).AsList();

        decimal totalIncome = await db.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(Amount), 0) FROM Transactions WHERE Type = 'Income'");
        UnallocatedCash = totalIncome - Envelopes.Sum(envelope => envelope.AllocatedAmount);
    }
}
