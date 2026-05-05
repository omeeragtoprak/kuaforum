using Kuaforum.Application.Common.Interfaces;
using Kuaforum.Domain.Modules.Identity;
using Kuaforum.Domain.Modules.Identity.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Persistence.Seeding;

public sealed class AdminSeederOptions
{
    public const string SectionName = "Seed:Admin";
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Founder";
    public string Password { get; set; } = string.Empty;
    public string? FixedTotpSecret { get; set; }
}

/// <summary>
/// Idempotent: if no Admin user exists and Seed:Admin:Email + Password are set,
/// creates the first admin with random TOTP secret + logs the provisioning URI
/// for the founder to scan into Authenticator.
/// </summary>
public sealed class AdminSeeder(
    ApplicationDbContext db,
    IPasswordHasher passwordHasher,
    ITotpService totp,
    ISymmetricEncryption encryption,
    IOptions<AdminSeederOptions> options,
    IHostEnvironment env,
    ILogger<AdminSeeder> logger)
{
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1) return "***";
        return email[..1] + new string('*', Math.Max(2, at - 1)) + email[at..];
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Email) || string.IsNullOrWhiteSpace(opts.Password))
        {
            logger.LogDebug("AdminSeeder: Seed:Admin:Email/Password missing, skipping.");
            return;
        }

        var existing = await db.Users.AnyAsync(u => u.Role == UserRole.Admin, ct);
        if (existing)
        {
            logger.LogDebug("AdminSeeder: admin already exists, skipping.");
            return;
        }

        var emailResult = EmailAddress.TryCreate(opts.Email);
        if (emailResult.IsFailure)
        {
            logger.LogWarning("AdminSeeder: invalid email {Email}, skipping.", opts.Email);
            return;
        }

        var userResult = User.CreateAdmin(emailResult.Value, opts.DisplayName);
        if (userResult.IsFailure) return;
        var user = userResult.Value;
        db.Users.Add(user);

        var totpSecret = string.IsNullOrEmpty(opts.FixedTotpSecret)
            ? totp.GenerateSecret()
            : opts.FixedTotpSecret;

        var cred = AdminCredential.Create(
            userId: user.Id,
            passwordHash: passwordHasher.Hash(opts.Password),
            totpSecretEncrypted: encryption.Encrypt(totpSecret));
        db.AdminCredentials.Add(cred);

        await db.SaveChangesAsync(ct);

        if (env.IsDevelopment())
        {
            var provisioningUri = totp.FormatProvisioningUri(totpSecret, opts.Email, "Kuaforum");
            logger.LogWarning("================================================================");
            logger.LogWarning("ADMIN USER CREATED [DEV ONLY]: {Email}", opts.Email);
            logger.LogWarning("TOTP SECRET (Base32) [DEV ONLY]: {Secret}", totpSecret);
            logger.LogWarning("Authenticator otpauth URI [DEV ONLY]: {Uri}", provisioningUri);
            logger.LogWarning("Authenticator app'a manuel olarak gir veya otpauth URI'yi QR kod jeneratörüne yapıştır.");
            logger.LogWarning("================================================================");
        }
        else
        {
            // Production: expose nothing sensitive in logs. Founder sets Seed:Admin:FixedTotpSecret out-of-band.
            logger.LogInformation("Admin user provisioned for {MaskedEmail} (TOTP secret intentionally not logged in non-Development env).",
                MaskEmail(opts.Email));
        }
    }
}
