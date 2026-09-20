using System.Security.Claims;
using System.Text;
using Cx.Core.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cx.Api.Infrastructure;

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public static SymmetricSecurityKey Key(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.SigningKey));

    public (string Token, DateTimeOffset ExpiresAt) Create(AppUser user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(ClaimNames.Subject, user.Id),
            new(ClaimNames.Name, user.DisplayName),
            new(ClaimNames.Role, user.Role.ToString()),
        };
        if (user.DealerId is not null) claims.Add(new Claim(ClaimNames.DealerId, user.DealerId));

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(Key(o), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}
