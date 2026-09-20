using System.Security.Cryptography;
using System.Text;
using Cx.Api.Infrastructure;
using Cx.Core.Data;
using Cx.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Cx.Api.Endpoints;

public sealed record LoginRequest(string Username, string Password);

public sealed record UserDto(string Id, string Username, string DisplayName, UserRole Role, string? DealerId);

public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", [AllowAnonymous] async (LoginRequest request, CxDatabase db, JwtTokenService tokens, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password) || request.Password.Length > 200)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid request", detail: "Username and password are required.");

            var user = await db.Users.Find(u => u.Username == request.Username.Trim().ToLowerInvariant()).FirstOrDefaultAsync(ct);

            // Same response for an unknown user and a wrong password. DEMO: passwords are stored in plain text by request.
            if (user is null || !FixedTimeEquals(user.Password, request.Password))
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Login failed", detail: "Invalid username or password.");

            var (token, expiresAt) = tokens.Create(user);
            return Results.Ok(new LoginResponse(token, expiresAt, ToDto(user)));
        }).RequireRateLimiting(Policies.LoginLimit);

        group.MapGet("/me", async (System.Security.Claims.ClaimsPrincipal principal, CxDatabase db, CancellationToken ct) =>
        {
            var id = principal.UserId();
            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync(ct);
            return user is null ? Results.Unauthorized() : Results.Ok(ToDto(user));
        });
    }

    private static UserDto ToDto(AppUser u) => new(u.Id, u.Username, u.DisplayName, u.Role, u.DealerId);

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
