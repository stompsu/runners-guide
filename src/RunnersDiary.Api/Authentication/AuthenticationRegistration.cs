using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace RunnersDiary.Api.Authentication;

public static class AuthenticationRegistration
{
    public const string GuestScheme = "GuestBearer";
    private const string EntraScheme = "EntraBearer";
    private const string SelectorScheme = "DiaryBearer";

    public static IServiceCollection AddDiaryAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        GuestTokenSettings guestSettings)
    {
        var authority = configuration["Auth:Entra:Authority"];
        var audience = configuration["Auth:Entra:Audience"];
        var hasEntraConfiguration = !string.IsNullOrWhiteSpace(authority)
            && !string.IsNullOrWhiteSpace(audience);

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SelectorScheme;
                options.DefaultChallengeScheme = SelectorScheme;
            })
            .AddPolicyScheme(SelectorScheme, SelectorScheme, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var authorization = context.Request.Headers.Authorization.FirstOrDefault();
                    var token = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                        ? authorization["Bearer ".Length..].Trim()
                        : null;

                    if (!string.IsNullOrWhiteSpace(token) && new JwtSecurityTokenHandler().CanReadToken(token))
                    {
                        try
                        {
                            if (new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer == guestSettings.Issuer)
                            {
                                return GuestScheme;
                            }
                        }
                        catch (ArgumentException)
                        {
                            return hasEntraConfiguration ? EntraScheme : GuestScheme;
                        }
                        catch (SecurityTokenException)
                        {
                            return hasEntraConfiguration ? EntraScheme : GuestScheme;
                        }
                    }

                    return hasEntraConfiguration ? EntraScheme : GuestScheme;
                };
            })
            .AddJwtBearer(GuestScheme, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = guestSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = guestSettings.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = guestSettings.SigningKey,
                    RoleClaimType = "role",
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            });

        if (hasEntraConfiguration)
        {
            services.AddAuthentication().AddJwtBearer(EntraScheme, options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    RoleClaimType = "roles",
                    NameClaimType = JwtRegisteredClaimNames.Sub
                };
            });
        }

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
