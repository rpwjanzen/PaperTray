using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Data;
//using Microsoft.Data.SqlClient; // Or Microsoft.Data.Sqlite / Npgsql depending on your DB
using Dapper;
using System.Text.Json;
using PaperTray.Models;
using StarFederation.Datastar.DependencyInjection;
using StarFederation.Datastar;
using System.Text.Json.Serialization;
using StarFederation.Datastar.ModelBinding;
using PaperTray.Services;

namespace PaperTray.Pages.Budget;

[IgnoreAntiforgeryToken]
public class IndexModel : PageModel
{
    public List<Envelope> Envelopes { get; set; } = new();
    public decimal UnallocatedCash { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Enter an envelope name.")]
    [StringLength(100, ErrorMessage = "Envelope names must be 100 characters or fewer.")]
    public string NewEnvelopeName { get; set; } = string.Empty;

    private readonly IDatastarService _datastar;
    private readonly Func<IDbConnection> _dbFactory;
    private readonly IViewRenderService _viewRenderService;

    public IndexModel(
        Func<IDbConnection> dbFactory,
        IDatastarService datastar,
        IViewRenderService viewRenderService)
    {
        _dbFactory = dbFactory;
        _datastar = datastar;
        _viewRenderService = viewRenderService;
    }

    // Initial page load
    public async Task OnGetAsync()
    {
        await LoadBudgetAsync();
    }

    public async Task<IActionResult> OnPostCreateEnvelopeAsync()
    {
        CreateEnvelopeSignals? signals = await _datastar.ReadSignalsAsync<CreateEnvelopeSignals>();
        NewEnvelopeName = signals?.NewEnvelopeName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(NewEnvelopeName))
        {
            await PatchCreateEnvelopeErrorAsync("Enter an envelope name.");
            return new EmptyResult();
        }

        if (NewEnvelopeName.Length > 100)
        {
            await PatchCreateEnvelopeErrorAsync("Envelope names must be 100 characters or fewer.");
            return new EmptyResult();
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
            new { CategoryName = NewEnvelopeName }
        );

        if (inserted == 0)
        {
            await PatchCreateEnvelopeErrorAsync(
                "An envelope with this name already exists.");
            return new EmptyResult();
        }

        var envelopes = (await db.QueryAsync<Envelope>(
            "SELECT * FROM Envelopes ORDER BY Id")).ToList();
        string envelopeRows = await _viewRenderService.RenderPartialToStringAsync(
            "/Pages/Budget/_EnvelopeRows.cshtml",
            envelopes);
        await _datastar.PatchElementsAsync(
            envelopeRows,
            new PatchElementsOptions
            {
                Selector = "#envelopes-body",
                PatchMode = ElementPatchMode.Inner,
            });
        await _datastar.PatchElementsAsync(
            "<span id=\"create-envelope-error\" aria-live=\"polite\"></span>",
            new PatchElementsOptions
            {
                Selector = "#create-envelope-error",
                PatchMode = ElementPatchMode.Replace,
            });
        await _datastar.PatchSignalsAsync(
            JsonSerializer.Serialize(new { newEnvelopeName = string.Empty }));

        return new EmptyResult();
    }

    public sealed class CreateEnvelopeSignals
    {
        public string? NewEnvelopeName { get; set; }
    }

    private async Task PatchCreateEnvelopeErrorAsync(string message)
    {
        string encodedMessage = System.Net.WebUtility.HtmlEncode(message);
        await _datastar.PatchElementsAsync(
            $"<span id=\"create-envelope-error\" aria-live=\"polite\">{encodedMessage}</span>",
            new PatchElementsOptions
            {
                Selector = "#create-envelope-error",
                PatchMode = ElementPatchMode.Replace,
            });
    }

    private async Task LoadBudgetAsync()
    {
        using var db = _dbFactory();
        
        var envelopes = await db.QueryAsync<Envelope>("SELECT * FROM Envelopes");
        Envelopes = envelopes.ToList();
        
        decimal totalIncome = await GetTotalIncomeAsync(db);
        decimal totalAllocated = Envelopes.Sum(e => e.AllocatedAmount);
        UnallocatedCash = totalIncome - totalAllocated;
    }

    public class MySignals {
        public decimal Allocated { get; set; } = 0m;
        public int Id { get; set; } = 0;
    }

    // Datastar updates this handler reactively when user types into the allocation field
    public async Task<IActionResult> OnPostUpdateAllocationAsync([FromQuery] int id)
    {
        AllocationSignals? signals = await _datastar.ReadSignalsAsync<AllocationSignals>();
        string allocatedSignal = $"allocated_{id}";
        if (signals is null
            || !signals.Values.TryGetValue(allocatedSignal, out JsonElement allocatedValue)
            || !allocatedValue.TryGetDecimal(out decimal allocated))
        {
            return BadRequest($"Missing allocation signal for envelope {id}.");
        }

        // 2. Perform the fast transactional database update via Dapper
        using var db = _dbFactory();
        await db.ExecuteAsync(
            "UPDATE Envelopes SET AllocatedAmount = @Allocated WHERE Id = @Id", 
            new { Allocated = allocated, Id = id }
        );

        // 3. Recalculate the overall "Unallocated Cash" pool
        decimal totalIncome = await GetTotalIncomeAsync(db);
        decimal totalAllocated = await db.ExecuteScalarAsync<decimal>("SELECT SUM(AllocatedAmount) FROM Envelopes");
        decimal updatedUnallocated = totalIncome - totalAllocated;

        // 4. Construct the HTML fragment to send back
        string htmlPatch = $"""
        <div id="unallocated-pool">
            Available to Budget: ${updatedUnallocated:F2}
        </div>
        """;

        // 5. Stream the patch cleanly using the official IDatastarService
        // The service automatically writes correct headers and flushes the response body stream.
        await _datastar.PatchElementsAsync(htmlPatch, new PatchElementsOptions
        {
            Selector = "#unallocated-pool",
            PatchMode = ElementPatchMode.Replace,
        });

        // Return an EmptyResult because Datastar keeps the stream open and reads chunks 
        // until the connection gracefully terminates.
        return new EmptyResult();
    }

    private static Task<decimal> GetTotalIncomeAsync(IDbConnection db)
    {
        return db.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(Amount), 0) FROM Transactions WHERE Type = 'Income'");
    }

    public sealed class AllocationSignals
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Values { get; set; } = new();
    }
}
