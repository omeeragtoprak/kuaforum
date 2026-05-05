using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Kuaforum.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Kuaforum.Api.Configuration;

public static class AuthSetup
{
    public static IServiceCollection AddKuaforumAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Disable Microsoft's inbound claim mapping so "role"/"sub" stay as-is
        // (otherwise they get rewritten to http://schemas.microsoft.com/... URIs and RequireRole fails).
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt options missing.");

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
            throw new InvalidOperationException("Jwt:SigningKey must be configured (32+ bytes).");

        var keyBytes = Encoding.UTF8.GetBytes(jwt.SigningKey);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = "sub",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("Customer", p => p.RequireRole("Customer"));
            options.AddPolicy("BarberOwner", p => p.RequireRole("BarberOwner"));
            options.AddPolicy("Stylist", p => p.RequireRole("Stylist", "BarberOwner"));
            options.AddPolicy("Admin", p => p.RequireRole("Admin"));
        });

        return services;
    }
}
