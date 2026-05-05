using System.Text.RegularExpressions;
using Kuaforum.Domain.Common;

namespace Kuaforum.Domain.Modules.Identity.ValueObjects;

public readonly partial record struct PhoneE164
{
    private static readonly Regex TrMobilePattern = TrMobileRegex();

    public string Value { get; }

    private PhoneE164(string value) => Value = value;

    public static Result<PhoneE164> TryCreate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Result.Failure<PhoneE164>(Error.Validation("Telefon numarası gerekli."));

        var trimmed = raw.Trim().Replace(" ", "");
        if (!TrMobilePattern.IsMatch(trimmed))
            return Result.Failure<PhoneE164>(
                Error.Validation("Telefon numarası +905XXXXXXXXX formatında Türkiye GSM numarası olmalı."));

        return new PhoneE164(trimmed);
    }

    public string Masked() => Value.Length >= 13
        ? $"{Value[..3]} {Value.Substring(3, 3)} •• •• {Value[^2..]}"
        : Value;

    public override string ToString() => Value;

    [GeneratedRegex(@"^\+905\d{9}$")]
    private static partial Regex TrMobileRegex();
}
