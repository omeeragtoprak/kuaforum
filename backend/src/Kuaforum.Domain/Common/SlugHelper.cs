using System.Globalization;
using System.Text;

namespace Kuaforum.Domain.Common;

public static class SlugHelper
{
    private static readonly Dictionary<char, char> TrLowerMap = new()
    {
        ['I'] = 'ı', ['İ'] = 'i', ['Ş'] = 'ş', ['Ğ'] = 'ğ', ['Ç'] = 'ç', ['Ö'] = 'ö', ['Ü'] = 'ü',
    };

    public static string Make(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(TrLowerMap.TryGetValue(ch, out var mapped) ? mapped : char.ToLower(ch, CultureInfo.GetCultureInfo("tr-TR")));
        }

        var lowered = sb.ToString();

        var ascii = new StringBuilder(lowered.Length);
        foreach (var ch in lowered)
        {
            ascii.Append(ch switch
            {
                'ı' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ç' => 'c',
                'ö' => 'o',
                'ü' => 'u',
                _ => ch,
            });
        }

        var result = new StringBuilder(ascii.Length);
        var prevDash = false;
        foreach (var ch in ascii.ToString())
        {
            if (char.IsLetterOrDigit(ch))
            {
                result.Append(ch);
                prevDash = false;
            }
            else if (!prevDash && result.Length > 0)
            {
                result.Append('-');
                prevDash = true;
            }
        }

        return result.ToString().TrimEnd('-');
    }
}
