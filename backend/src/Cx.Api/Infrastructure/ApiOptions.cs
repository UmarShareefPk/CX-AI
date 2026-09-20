using System.ComponentModel.DataAnnotations;

namespace Cx.Api.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>HMAC signing key. Supply through user-secrets or the Jwt__SigningKey environment variable, never the repo.</summary>
    [Required, MinLength(32)] public string SigningKey { get; set; } = "";
    [Required] public string Issuer { get; set; } = "cx-api";
    [Required] public string Audience { get; set; } = "cx-web";
    [Range(5, 1440)] public int ExpiryMinutes { get; set; } = 480;
}

public sealed class CorsOptions
{
    public const string Section = "Cors";

    [Required, MinLength(1)] public string[] AllowedOrigins { get; set; } = [];
}

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    [Range(1, 100000)] public int GlobalPerMinute { get; set; } = 600;
    [Range(1, 1000)] public int LoginPerMinute { get; set; } = 10;
    [Range(1, 1000)] public int ChatPerMinute { get; set; } = 20;
}
