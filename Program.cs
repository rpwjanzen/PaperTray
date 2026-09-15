using System.Data;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarFederation.Datastar.DependencyInjection;
using PaperTray.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Datastar Services
builder.Services.AddDatastar()
    .AddJsonOptions(options =>
    {
        options.Converters.Add(new JsonStringEnumConverter());
    });

// 2. Register SQLite Connection Factory for Dapper
// Registering as a Func<IDbConnection> ensures a fresh connection is created 
// whenever a Razor Page requests it, and properly disposed of after use.
builder.Services.AddScoped<Func<IDbConnection>>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("DefaultConnection");
    
    return () => new SqliteConnection(connectionString);
});

// 3. Register Razor Pages
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IViewRenderService, RazorViewRenderService>();

var app = builder.Build();

// 4. (Optional) Run Database Initializer migrations on startup
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

    -- Seed an initial profile/income if empty
    INSERT OR IGNORE INTO Settings (Id, TotalIncome) VALUES (1, 5000.00);
    """;
    
    command.CommandText = sqlTemplate;
    command.ExecuteNonQuery();
}
