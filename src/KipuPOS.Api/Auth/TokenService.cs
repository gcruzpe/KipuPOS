using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using KipuPOS.Api.Domain;
using KipuPOS.Api.Support;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace KipuPOS.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string Key { get; set; } = "";
    public int ExpiresMinutes { get; set; } = 480;
}

public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public (string Token, DateTime ExpiresUtc) Create(User user)
    {
        var expires = DateTime.UtcNow.AddMinutes(_o.ExpiresMinutes);
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Key)), SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
        };
        if (user.TenantId is { } tenantId) claims.Add(new Claim(AppClaims.Tenant, tenantId.ToString()));
        if (user.BranchId is { } branchId) claims.Add(new Claim(AppClaims.Branch, branchId.ToString()));
        // "mcp" = debe cambiar la contraseña: con este token solo se puede llamar a change-password
        if (user.MustChangePassword) claims.Add(new Claim(AppClaims.MustChangePassword, "1"));
        var jwt = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
