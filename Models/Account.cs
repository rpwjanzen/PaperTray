namespace PaperTray.Models;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsOnBudget { get; set; } = true;
}
