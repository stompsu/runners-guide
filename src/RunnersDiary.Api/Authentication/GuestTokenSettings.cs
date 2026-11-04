using Microsoft.IdentityModel.Tokens;

namespace RunnersDiary.Api.Authentication;

public sealed class GuestTokenSettings
{
    private GuestTokenSettings(string issuer, string audience, SymmetricSecurityKey signingKey)
    {
        Issuer = issuer;
        Audience = audience;
        SigningKey = signingKey;
    }

    public string Issuer { get; }
    public string Audience { get; }
    public SymmetricSecurityKey SigningKey { get; }

    public static GuestTokenSettings Load(IConfiguration configuration, IHostEnvironment environment)
    {
        var issuer = configuration["Auth:Guest:Issuer"] ?? "runners-diary-guest";
        var audience = configuration["Auth:Guest:Audience"] ?? "runners-diary-api";
        var encodedKey = configuration["Auth:Guest:SigningKey"];

        byte[] key;
        if (string.IsNullOrWhiteSpace(encodedKey) && environment.IsDevelopment())
        {
            key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        }
        else if (!string.IsNullOrWhiteSpace(encodedKey))
        {
            try
            {
                key = Convert.FromBase64String(encodedKey);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("Auth:Guest:SigningKey must be a base64-encoded key.", exception);
            }
        }
        else
        {
            throw new InvalidOperationException("Auth:Guest:SigningKey must be configured outside Development.");
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException("Auth:Guest:SigningKey must contain at least 256 bits.");
        }

        return new GuestTokenSettings(issuer, audience, new SymmetricSecurityKey(key));
    }
}
