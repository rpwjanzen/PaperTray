using System.ComponentModel.DataAnnotations;
using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PaperTray.Models;

namespace PaperTray.Pages.Transactions;

public class IndexModel : PageModel
{
    private readonly Func<IDbConnection> _dbFactory;

    public List<TransactionRow> Transactions { get; private set; } = new();
    public List<Account> Accounts { get; private set; } = new();
    public List<Envelope> Envelopes { get; private set; } = new();

    [BindProperty]
    public TransactionInput Input { get; set; } = new();

    public bool IsEditing => Input.Id.HasValue;

    public IndexModel(Func<IDbConnection> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IActionResult> OnGetAsync(int? editId = null)
    {
        await LoadLookupsAsync();

        if (editId.HasValue)
        {
            using var db = _dbFactory();
            var transaction = await db.QuerySingleOrDefaultAsync<TransactionInput>(
                """
                SELECT Id, Payee, AccountId, EnvelopeId, Amount, Type, Date, IsCleared
                FROM Transactions
                WHERE Id = @Id
                """,
                new { Id = editId.Value });

            if (transaction is null)
            {
                return NotFound();
            }

            Input = transaction;
        }

        await LoadTransactionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Input ??= new();
        NormalizeInput();
        await LoadLookupsAsync();

        if (!ModelState.IsValid || !await RelationshipsAreValidAsync())
        {
            await LoadTransactionsAsync();
            return Page();
        }

        using var db = _dbFactory();
        if (Input.Id.HasValue)
        {
            int updated = await db.ExecuteAsync(
                """
                UPDATE Transactions
                SET Payee = @Payee,
                    AccountId = @AccountId,
                    EnvelopeId = @EnvelopeId,
                    Amount = @Amount,
                    Type = @Type,
                    Date = @Date,
                    IsCleared = @IsCleared
                WHERE Id = @Id
                """,
                Input);

            if (updated == 0)
            {
                return NotFound();
            }
        }
        else
        {
            await db.ExecuteAsync(
                """
                INSERT INTO Transactions
                    (Payee, AccountId, EnvelopeId, Amount, Type, Date, IsCleared)
                VALUES
                    (@Payee, @AccountId, @EnvelopeId, @Amount, @Type, @Date, @IsCleared)
                """,
                Input);
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        using var db = _dbFactory();
        await db.ExecuteAsync("DELETE FROM Transactions WHERE Id = @Id", new { Id = id });
        return RedirectToPage();
    }

    private void NormalizeInput()
    {
        Input.Payee = Input.Payee?.Trim() ?? string.Empty;
        if (Input.Date == default)
        {
            Input.Date = DateTime.Today;
        }
    }

    private async Task<bool> RelationshipsAreValidAsync()
    {
        using var db = _dbFactory();
        bool accountExists = await db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM Accounts WHERE Id = @Id)",
            new { Id = Input.AccountId });
        if (!accountExists)
        {
            ModelState.AddModelError(nameof(Input.AccountId), "Select an account.");
        }

        if (Input.EnvelopeId.HasValue)
        {
            bool envelopeExists = await db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM Envelopes WHERE Id = @Id)",
                new { Id = Input.EnvelopeId.Value });
            if (!envelopeExists)
            {
                ModelState.AddModelError(nameof(Input.EnvelopeId), "Select a valid envelope.");
            }
        }

        return ModelState.IsValid;
    }

    private async Task LoadLookupsAsync()
    {
        using var db = _dbFactory();
        Accounts = (await db.QueryAsync<Account>(
            "SELECT Id, Name, IsOnBudget FROM Accounts ORDER BY Name")).AsList();
        Envelopes = (await db.QueryAsync<Envelope>(
            "SELECT Id, CategoryName, AllocatedAmount, SpentAmount FROM Envelopes ORDER BY CategoryName")).AsList();
    }

    private async Task LoadTransactionsAsync()
    {
        using var db = _dbFactory();
        Transactions = (await db.QueryAsync<TransactionRow>(
            """
            SELECT t.Id, t.Payee, t.AccountId, a.Name AS AccountName,
                   t.EnvelopeId, e.CategoryName AS EnvelopeName,
                   t.Amount, t.Type, t.Date, t.IsCleared
            FROM Transactions t
            INNER JOIN Accounts a ON a.Id = t.AccountId
            LEFT JOIN Envelopes e ON e.Id = t.EnvelopeId
            ORDER BY t.Date DESC, t.Id DESC
            """)).AsList();
    }

    public sealed class TransactionInput
    {
        public int? Id { get; set; }

        [Required(ErrorMessage = "Enter a payee.")]
        [StringLength(200, ErrorMessage = "Payees must be 200 characters or fewer.")]
        public string Payee { get; set; } = string.Empty;

        [Range(1, int.MaxValue, ErrorMessage = "Select an account.")]
        public int AccountId { get; set; }

        public int? EnvelopeId { get; set; }

        [Range(typeof(decimal), "0.01", "79228162514264337593543950335",
            ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public TransactionType Type { get; set; } = TransactionType.Expense;

        [DataType(DataType.Date)]
        public DateTime Date { get; set; } = DateTime.Today;

        public bool IsCleared { get; set; }
    }

    public sealed class TransactionRow
    {
        public int Id { get; set; }
        public string Payee { get; set; } = string.Empty;
        public int AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public int? EnvelopeId { get; set; }
        public string? EnvelopeName { get; set; }
        public decimal Amount { get; set; }
        public TransactionType Type { get; set; }
        public DateTime Date { get; set; }
        public bool IsCleared { get; set; }
    }
}
