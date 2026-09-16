using System.Data;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// Register a fresh SQLite connection for each Razor Page operation.
// Registering as a Func<IDbConnection> ensures a fresh connection is created 
// whenever a Razor Page requests it, and properly disposed of after use.
builder.Services.AddScoped<Func<IDbConnection>>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    
    return () => new SqliteConnection(connectionString);
});

builder.Services.AddRazorPages();

var app = builder.Build();

await InitializeDatabaseAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.Run();

// Simple automated schema setup for our Envelope Budgeting tables
async Task InitializeDatabaseAsync(WebApplication webApp)
{
    using var scope = webApp.Services.CreateScope();
    var connectionFactory = scope.ServiceProvider.GetRequiredService<Func<IDbConnection>>();
    using var db = connectionFactory();
    
    // Dapper executes raw table constraints safely
    using var command = db.CreateCommand();
    db.Open();
    
    string sqlTemplate = """
    PRAGMA foreign_keys = ON;

    CREATE TABLE IF NOT EXISTS Settings (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        TotalIncome REAL NOT NULL
    );

    CREATE TABLE IF NOT EXISTS Envelopes (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        CategoryName TEXT NOT NULL,
        AllocatedAmount REAL NOT NULL,
        SpentAmount REAL NOT NULL
    );

    CREATE UNIQUE INDEX IF NOT EXISTS IX_Envelopes_CategoryName_NoCase
        ON Envelopes (CategoryName COLLATE NOCASE);

    CREATE TABLE IF NOT EXISTS Accounts (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Name TEXT NOT NULL,
        IsOnBudget INTEGER NOT NULL DEFAULT 1
    );

    CREATE UNIQUE INDEX IF NOT EXISTS IX_Accounts_Name_NoCase
        ON Accounts (Name COLLATE NOCASE);

    CREATE TABLE IF NOT EXISTS Transactions (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        Payee TEXT NOT NULL,
        AccountId INTEGER NOT NULL,
        EnvelopeId INTEGER NULL,
        Amount REAL NOT NULL,
        Type TEXT NOT NULL CHECK (Type IN ('Expense', 'Income', 'Transfer')),
        Date TEXT NOT NULL,
        IsCleared INTEGER NOT NULL DEFAULT 0,
        FOREIGN KEY (AccountId) REFERENCES Accounts (Id),
        FOREIGN KEY (EnvelopeId) REFERENCES Envelopes (Id)
    );

    -- Seed an initial profile/income if empty
    INSERT OR IGNORE INTO Settings (Id, TotalIncome) VALUES (1, 5000.00);
    """;
    
    command.CommandText = sqlTemplate;
    command.ExecuteNonQuery();
}
