using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SadcOrders.Services;

public sealed class MockAuthOptions
{
    public const string SectionName = "Auth:Mock";

    public string Issuer { get; set; } = "https://sadc-orders.local/mock-issuer";

    public string Audience { get; set; } = "api://sadc-orders";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Development only: never use mock mode in production.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int TokenLifetimeMinutes { get; set; } = 60;

    public SymmetricSecurityKey CreateKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Auth:Mock:SigningKey must be at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}

/// <summary>
/// Issues tokens shaped like Entra access tokens (iss, aud, sub, oid, name, and a "roles" array), so
/// the authorization code behaves the same in mock and real mode.
/// </summary>
public sealed class MockTokenIssuer
{
    private readonly MockAuthOptions _options;
    private readonly TimeProvider _time;

    public MockTokenIssuer(IOptions<MockAuthOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;
    }

    public (string Token, DateTime ExpiresAt) Issue(IEnumerable<string> roles, string userName = "Dev User")
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes);
        var subject = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("oid", subject),
            new("name", userName),
        };
        claims.AddRange(roles.Select(r => new Claim("roles", r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = new SigningCredentials(_options.CreateKey(), SecurityAlgorithms.HmacSha256),
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }
}
