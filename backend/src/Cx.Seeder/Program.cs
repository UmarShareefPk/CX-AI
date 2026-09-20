using Cx.Ai;
using Cx.Core;
using Cx.Core.Data;
using Cx.Core.Rag;
using Cx.Core.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Usage: dotnet run --project src/Cx.Seeder -- [--reset] [--skip-policies] [--reindex]
//   (no flags)      seed demo data if the database is empty, and (re)index policy chunks if needed
//   --reset         drop and regenerate dealers, customers, survey responses, users and conversations
//   --reindex       re-embed every policy chunk even if unchanged
//   --skip-policies do not call the embedding model
var flags = args.ToHashSet(StringComparer.OrdinalIgnoreCase);

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddCxSharedSettings();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddCxCore(builder.Configuration).AddCxAi(builder.Configuration);
using var host = builder.Build();

var seeder = host.Services.GetRequiredService<DemoDataSeeder>();
var db = host.Services.GetRequiredService<CxDatabase>();

await db.PingAsync();
await db.EnsureIndexesAsync();
Console.WriteLine($"Connected to MongoDB database '{db.Db.DatabaseNamespace.DatabaseName}'.");

if (flags.Contains("--reset")) await seeder.ResetAsync();

if (await seeder.HasDataAsync())
{
    Console.WriteLine("Demo data already present (use --reset to regenerate).");
}
else
{
    var data = await seeder.SeedAsync();
    Console.WriteLine($"Seeded {data.Dealers.Count} dealers, {data.Customers.Count} customers, {data.Responses.Count} survey responses, {data.Users.Count} users.");
    Console.WriteLine();
    Console.WriteLine("Login credentials (plain text by design for this demo):");
    Console.WriteLine($"  {"username",-10} {"password",-22} {"role",-7} dealer");
    foreach (var u in data.Users)
        Console.WriteLine($"  {u.Username,-10} {u.Password,-22} {u.Role,-7} {(u.DealerId is null ? "(all dealers)" : $"{u.DealerId} {u.DisplayName}")}");
    Console.WriteLine();
}

if (flags.Contains("--skip-policies"))
{
    Console.WriteLine("Skipped policy indexing.");
    return 0;
}

try
{
    var result = await host.Services.GetRequiredService<PolicyIngestor>().EnsureIndexedAsync(flags.Contains("--reindex"));
    Console.WriteLine($"Policy index ready: {result.Total} chunks ({result.Embedded} embedded, {result.Unchanged} unchanged, {result.Removed} removed) " +
                      $"with {result.EmbeddingModel}, {result.Dimensions} dimensions.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Policy indexing failed: {ex.Message}");
    Console.Error.WriteLine("Is the embedding model available? For the default: ollama pull embeddinggemma");
    return 1;
}

return 0;
