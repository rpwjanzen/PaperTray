namespace PaperTray.Models;

public class Envelope
{
    public int Id { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal AllocatedAmount { get; set; }
    public decimal SpentAmount { get; set; }
    public string? RenameError { get; set; }
    
    // Calculated property for Datastar local bindings
    public decimal Balance => AllocatedAmount - SpentAmount;
}
