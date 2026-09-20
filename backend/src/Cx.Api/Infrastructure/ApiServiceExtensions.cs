using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Cx.Ai;
using Cx.Core.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Cx.Api.Infrastructure;

public static class Policies
{
    public const string Admin = "Admin";
    public const string LoginLimit = "login";
    public const string ChatLimit = "chat";
}

public static class ApiServiceExtensions
{
    public static IServiceCollection AddCxApi(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<CorsOptions>().Bind(config.GetSection(CorsOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<RateLimitOptions>().Bind(config.GetSection(RateLimitOptions.Section)).ValidateDataAnnotations().ValidateOnStart();

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddProblemDetails();
        services.AddExceptionHandler<ProblemExceptionHandler>();

        services.AddSingleton<JwtTokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false; // keep "sub"/"role"/"dealer_id" as issued
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = JwtTokenService.Key(jwt.Value),
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = ClaimNames.Name,
                    RoleClaimType = ClaimNames.Role,
                };
            });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin)));

        services.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins(config.GetSection(CorsOptions.Section).Get<CorsOptions>()?.AllowedOrigins ?? [])
            .AllowAnyHeader().AllowAnyMethod()));

        services.AddRateLimiter(o =>
        {
            var limits = config.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                ctx.Request.Path.StartsWithSegments("/health")
                    ? RateLimitPartition.GetNoLimiter("health")
                    : Fixed($"ip:{Ip(ctx)}", limits.GlobalPerMinute));
            o.AddPolicy(Policies.LoginLimit, ctx => Fixed($"login:{Ip(ctx)}", limits.LoginPerMinute));
            o.AddPolicy(Policies.ChatLimit, ctx => Fixed($"chat:{ctx.User.FindFirst(ClaimNames.Subject)?.Value ?? Ip(ctx)}", limits.ChatPerMinute));
        });

        services.AddHealthChecks().AddCheck<MongoHealthCheck>("mongodb", tags: ["ready"]);

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("cx-api"))
            .WithTracing(t => t.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddSource(ChatClientFactory.TelemetrySource))
            .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddMeter(ChatClientFactory.TelemetrySource));
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
            otel.UseOtlpExporter();

        return services;
    }

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static RateLimitPartition<string> Fixed(string key, int perMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
        });
}
