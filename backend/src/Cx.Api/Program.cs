using Cx.Ai;
using Cx.Api.Endpoints;
using Cx.Api.Infrastructure;
using Cx.Core;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddCxSharedSettings();

// The token signing key must come from user-secrets (Development only) or the Jwt__SigningKey environment variable.
var ephemeralKey = false;
if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:SigningKey"]))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            $"Jwt:SigningKey is not configured (environment: {builder.Environment.EnvironmentName}). User-secrets are only loaded in Development. " +
            "Set the Jwt__SigningKey environment variable (32+ characters), or run with ASPNETCORE_ENVIRONMENT=Development.");

    // Development convenience: a random key per process. Logins survive until the API restarts; nothing is written anywhere.
    builder.Configuration.AddInMemoryCollection([new("Jwt:SigningKey", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)))]);
    ephemeralKey = true;
}

builder.Services
    .AddCxCore(builder.Configuration)
    .AddCxAi(builder.Configuration)
    .AddCxAgent(builder.Configuration)
    .AddCxApi(builder.Configuration);

var app = builder.Build();

if (ephemeralKey)
    app.Logger.LogWarning("No Jwt:SigningKey configured: using a temporary random key, so users must sign in again after every restart. " +
                          "Persist one with: dotnet user-secrets set \"Jwt:SigningKey\" \"<32+ characters>\" --project backend/src/Cx.Api");

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// Liveness: the process is up. Readiness: its dependencies (MongoDB) answer.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

app.MapAuth();
app.MapData();
app.MapAi();

app.Run();

/// <summary>Exposed so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
