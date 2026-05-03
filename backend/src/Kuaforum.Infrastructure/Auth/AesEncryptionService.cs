using System.Security.Cryptography;
using System.Text;
using Kuaforum.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace Kuaforum.Infrastructure.Auth;

/// <summary>
/// AES-256-GCM symmetric encryption for sensitive at-rest data (TOTP secrets, etc.).
/// Key is derived from configuration. Production: rotate the key annually + store in
/// vault, not appsettings.
/// </summary>
public sealed class AesEncryptionService(IOptions<EncryptionOptions> options) : ISymmetricEncryption
{
    private readonly byte[] _key = DeriveKey(options.Value.Key);

    public string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return string.Empty;

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plain);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, combined, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;

        var combined = Convert.FromBase64String(base64);
        var nonce = combined[..12];
        var tag = combined[12..28];
        var cipher = combined[28..];
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, tagSizeInBytes: 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DeriveKey(string secret)
    {
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Encryption:Key is required.");
        // SHA-256 → 32 bytes for AES-256
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";
    public string Key { get; set; } = string.Empty;
}
