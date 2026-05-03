using Kuaforum.Application.Common.Interfaces;
using Microsoft.AspNetCore.Identity;

namespace Kuaforum.Infrastructure.Auth;

public sealed class PasswordHasherService : IPasswordHasher
{
    private sealed class HashOwner { }

    private readonly PasswordHasher<HashOwner> _hasher = new();
    private static readonly HashOwner Owner = new();

    public string Hash(string password) => _hasher.HashPassword(Owner, password);

    public bool Verify(string hash, string candidate)
    {
        var result = _hasher.VerifyHashedPassword(Owner, hash, candidate);
        return result is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
