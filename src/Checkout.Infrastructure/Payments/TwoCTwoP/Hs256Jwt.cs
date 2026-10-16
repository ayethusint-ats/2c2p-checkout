using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Checkout.Infrastructure.Payments.TwoCTwoP;

/// <summary>
/// Minimal HS256 JWT (RFC 7519) sign/verify for 2C2P payloads.
/// Only HS256 is supported; any other "alg" (including "none") is rejected.
/// </summary>
internal static class Hs256Jwt
{
    private const string Algorithm = "HS256";

    // Fixed header, so every token we sign starts with the same first segment.
    private static readonly string EncodedHeader =
        Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);

    public static string Sign(string payloadJson, string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);

        var signingInput = EncodedHeader + "." + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        return signingInput + "." + Base64Url.EncodeToString(ComputeSignature(signingInput, secret));
    }

    /// <summary>
    /// Returns true and the decoded payload only if the token is well formed, declares HS256,
    /// and its signature matches <paramref name="secret"/>. Never throws on bad input.
    /// </summary>
    public static bool TryVerify(string jwt, string secret, out string payloadJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        payloadJson = string.Empty;

        if (string.IsNullOrEmpty(jwt))
            return false;

        var parts = jwt.Split('.');
        if (parts.Length != 3)
            return false;

        try
        {
            // Check the header before anything else: the token must not choose its own algorithm.
            using (var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])))
            {
                if (header.RootElement.ValueKind != JsonValueKind.Object
                    || !header.RootElement.TryGetProperty("alg", out var alg)
                    || alg.ValueKind != JsonValueKind.String
                    || alg.GetString() != Algorithm)
                {
                    return false;
                }
            }

            // Recompute over the segments exactly as received, then compare in constant time.
            var expected = ComputeSignature(parts[0] + "." + parts[1], secret);
            var actual = Base64Url.DecodeFromChars(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                return false;

            payloadJson = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1]));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }

    private static byte[] ComputeSignature(string signingInput, string secret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signingInput));
}
