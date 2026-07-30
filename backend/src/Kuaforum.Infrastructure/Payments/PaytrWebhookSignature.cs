using System.Security.Cryptography;
using System.Text;

namespace Kuaforum.Infrastructure.Payments;

public static class PaytrWebhookSignature
{
    /// <summary>
    /// PayTR bildirim (callback) hash formülü: base64(HMAC-SHA256(merchant_oid + merchant_salt
    /// + status + total_amount, key: merchant_key)). Resmi dokümandaki C# örneğiyle birebir
    /// doğrulandı (https://dev.paytr.com/en/iframe-api/iframe-api-2-adim, 2026-07-30).
    /// </summary>
    public static string Compute(string merchantOid, string merchantSalt, string status, string totalAmount, string merchantKey)
    {
        var data = string.Concat(merchantOid, merchantSalt, status, totalAmount);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(merchantKey));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }
}
