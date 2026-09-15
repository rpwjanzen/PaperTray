using System.ComponentModel.DataAnnotations;

namespace PaperTray.Models;

public enum TransactionType
{
    Expense,
    Income,
    Transfer,
}

public class Transaction
{
    public int Id { get; set; }

    [Required]
    public string Payee { get; set; } = string.Empty;

    public int AccountId { get; set; }
    public int? EnvelopeId { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public DateTime Date { get; set; }
    public bool IsCleared { get; set; }
}
