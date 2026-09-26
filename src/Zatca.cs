using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ZatcaPos;

/// <summary>
/// ZATCA (Saudi e-invoicing) helpers for simplified tax invoices.
/// Phase 1 QR: Base64 of TLV records (tag byte, length byte, UTF-8 value) for
///   1 seller name, 2 VAT registration number, 3 timestamp, 4 invoice total incl. VAT, 5 VAT total.
/// Each invoice also keeps a UUID, a running counter (ICV) and a hash chain (PIH), which Phase 2
/// integration needs, so the data is ready when the POS is connected to the Fatoora platform.
/// </summary>
public static class Zatca
{
    public const decimal VatRate = 0.15m;

    public static string BuildQr(string sellerName, string vatNumber, DateTime timestampUtc, decimal totalWithVat, decimal vatTotal)
    {
        using var ms = new MemoryStream();
        void Tlv(byte tag, string value)
        {
            byte[] v = Encoding.UTF8.GetBytes(value);
            if (v.Length > 255) throw new ArgumentException($"TLV value for tag {tag} is longer than 255 bytes.");
            ms.WriteByte(tag); ms.WriteByte((byte)v.Length); ms.Write(v);
        }
        Tlv(1, sellerName);
        Tlv(2, vatNumber);
        Tlv(3, timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        Tlv(4, Money(totalWithVat));
        Tlv(5, Money(vatTotal));
        return Convert.ToBase64String(ms.ToArray());
    }

    public static Dictionary<int, string> ParseQr(string base64)
    {
        byte[] b = Convert.FromBase64String(base64);
        var result = new Dictionary<int, string>();
        for (int i = 0; i < b.Length;)
        {
            int tag = b[i], len = b[i + 1];
            result[tag] = Encoding.UTF8.GetString(b, i + 2, len);
            i += 2 + len;
        }
        return result;
    }

    /// <summary>A Saudi VAT number is 15 digits, starting and ending with 3.</summary>
    public static bool IsValidVatNumber(string vat) => vat.Length == 15 && vat.All(char.IsAsciiDigit) && vat[0] == '3' && vat[^1] == '3';

    public static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static string Money(decimal v) => Round(v).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Local hash chain: each invoice hash covers its own key fields plus the previous invoice hash, so
    /// tampering with a stored sale breaks the chain. Phase 2 replaces the input with the canonical UBL XML.
    /// </summary>
    public static string InvoiceHash(string previousHash, string invoiceNumber, string uuid, DateTime issuedUtc, decimal total, decimal vat)
    {
        string canonical = string.Join('|', previousHash, invoiceNumber, uuid, issuedUtc.ToString("O", CultureInfo.InvariantCulture), Money(total), Money(vat));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>ZATCA's documented PIH for the first invoice: base64 of the hex SHA-256 of "0".</summary>
    public static readonly string FirstPreviousHash =
        Convert.ToBase64String(Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData("0"u8))));
}
