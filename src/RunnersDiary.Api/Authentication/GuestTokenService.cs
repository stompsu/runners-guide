using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using RunnersDiary.Domain;

namespace RunnersDiary.Api.Authentication;

public sealed class GuestTokenService(
    GuestTokenSettings settings,
    TimeProvider timeProvider)
{
    public GuestToken Create(DiaryUser guest)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = guest.ExpiresAtUtc
            ?? throw new InvalidOperationException("Guest accounts must have an expiration time.");
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, guest.Id),
                new Claim("role", "guest")
            ],
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256));

        return new GuestToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public sealed record GuestToken(string AccessToken, DateTimeOffset ExpiresAtUtc);
