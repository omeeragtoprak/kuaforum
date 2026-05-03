using System.Net.Mail;
using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.ValueObjects;

public readonly record struct EmailAddress
{
    public string Value { get; }
    public string Normalized => Value.ToLowerInvariant();

    private EmailAddress(string value) => Value = value;

    public static Result<EmailAddress> TryCreate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<EmailAddress>(Error.Validation("Email is required."));

        var trimmed = raw.Trim();
        if (trimmed.Length > 256)
            return Result.Failure<EmailAddress>(Error.Validation("Email must be at most 256 chars."));

        if (!MailAddress.TryCreate(trimmed, out var addr) || addr is null)
            return Result.Failure<EmailAddress>(Error.Validation("Email format invalid."));

        return new EmailAddress(addr.Address);
    }

    public string Masked()
    {
        var at = Value.IndexOf('@');
        if (at <= 1) return Value;
        return $"{Value[0]}{new string('*', at - 1)}{Value[at..]}";
    }

    public override string ToString() => Value;
}
