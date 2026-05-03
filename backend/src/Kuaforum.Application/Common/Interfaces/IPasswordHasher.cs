namespace Kuaforum.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string hash, string candidate);
}

public interface ITotpService
{
    string GenerateSecret();
    bool Verify(string base32Secret, string code, int windowSize = 1);
    string FormatProvisioningUri(string base32Secret, string accountName, string issuer);
}

public interface ISymmetricEncryption
{
    string Encrypt(string plain);
    string Decrypt(string cipher);
}
