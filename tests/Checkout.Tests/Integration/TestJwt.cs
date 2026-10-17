using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Checkout.Tests.Integration;

/// <summary>
/// The tests' own HS256 implementation, independent of production's Hs256Jwt, so a bug there
/// can't be hidden by the same bug on the "2C2P" side.
/// </summary>
internal static class TestJwt
{
    public static string Sign(string payloadJson, string secret)
    {
        var header = Base64Url.EncodeToString("""{"alg":"HS256","typ":"JWT"}"""u8);
        var body = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = header + "." + body;
        return signingInput + "." + Base64Url.EncodeToString(Hmac(signingInput, secret));
    }

    public static string Sign(object payload, string secret) => Sign(JsonSerializer.Serialize(payload), secret);

    public static bool TryVerify(string jwt, string secret, out JsonElement payload)
    {
        payload = default;
        var parts = jwt.Split('.');
        if (parts.Length != 3)
            return false;

        var expected = Hmac(parts[0] + "." + parts[1], secret);
        if (!CryptographicOperations.FixedTimeEquals(expected, Base64Url.DecodeFromChars(parts[2])))
            return false;

        payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement.Clone();
        return true;
    }

    private static byte[] Hmac(string input, string secret) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(input));
}
