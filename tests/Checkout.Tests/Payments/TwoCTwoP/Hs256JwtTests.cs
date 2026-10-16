using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Checkout.Infrastructure.Payments.TwoCTwoP;

namespace Checkout.Tests.Payments.TwoCTwoP;

public class Hs256JwtTests
{
    // Sample from 2C2P's JWT documentation.
    private const string DocSecret = "1F4EDB965BBB3094F791A83750FBDCDA3852131CB5218E21E4F42929A1975E60";
    private const string DocPayload =
        """{"merchantID":"JT01","invoiceNo":"1523953661","description":"item 1","amount":1000,"currencyCode":"SGD"}""";
    private const string DocToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
        + "eyJtZXJjaGFudElEIjoiSlQwMSIsImludm9pY2VObyI6IjE1MjM5NTM2NjEiLCJkZXNjcmlwdGlvbiI6Iml0ZW0gMSIsImFtb3VudCI6MTAwMCwiY3VycmVuY3lDb2RlIjoiU0dEIn0."
        + "ElmhOULUdK63FnriSs2XyhH1LxULoTVIopTUuQ77DvA";

    [Fact]
    public void Sign_produces_the_2c2p_documentation_sample()
    {
        Assert.Equal(DocToken, Hs256Jwt.Sign(DocPayload, DocSecret));
    }

    [Fact]
    public void TryVerify_accepts_a_token_it_signed_and_returns_the_payload()
    {
        var token = Hs256Jwt.Sign(DocPayload, DocSecret);

        Assert.True(Hs256Jwt.TryVerify(token, DocSecret, out var payload));
        Assert.Equal(DocPayload, payload);
    }

    [Fact]
    public void TryVerify_accepts_the_2c2p_documentation_sample()
    {
        Assert.True(Hs256Jwt.TryVerify(DocToken, DocSecret, out var payload));
        Assert.Equal(DocPayload, payload);
    }

    [Fact]
    public void TryVerify_rejects_a_wrong_secret()
    {
        Assert.False(Hs256Jwt.TryVerify(DocToken, "not-the-secret", out var payload));
        Assert.Equal(string.Empty, payload);
    }

    [Fact]
    public void TryVerify_rejects_a_tampered_payload()
    {
        var parts = DocToken.Split('.');
        var tampered = DocPayload.Replace("\"amount\":1000", "\"amount\":1");
        var token = $"{parts[0]}.{Encode(tampered)}.{parts[2]}";

        Assert.False(Hs256Jwt.TryVerify(token, DocSecret, out _));
    }

    [Fact]
    public void TryVerify_rejects_alg_none_without_signature()
    {
        var token = $"{Encode("""{"alg":"none","typ":"JWT"}""")}.{Encode(DocPayload)}.";

        Assert.False(Hs256Jwt.TryVerify(token, DocSecret, out _));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS512")]
    [InlineData("hs256")]
    public void TryVerify_rejects_other_algorithms_even_with_a_valid_hmac(string alg)
    {
        // Correct HMAC-SHA256 over the segments: only the declared algorithm is wrong.
        var signingInput = $"{Encode($$"""{"alg":"{{alg}}","typ":"JWT"}""")}.{Encode(DocPayload)}";
        var signature = Base64Url.EncodeToString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(DocSecret), Encoding.UTF8.GetBytes(signingInput)));

        Assert.False(Hs256Jwt.TryVerify($"{signingInput}.{signature}", DocSecret, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("...")]
    [InlineData("!!!.###.$$$")]
    [InlineData("eyJub3QganNvbg.eyJ9.c2ln")]                              // header is not JSON
    [InlineData("WyJhcnJheSJd.eyJ9.c2ln")]                                // header is a JSON array
    [InlineData("eyJhbGciOjF9.eyJ9.c2ln")]                                // alg is a number
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ9.%%%")]         // signature not base64url
    public void TryVerify_returns_false_for_malformed_input(string jwt)
    {
        Assert.False(Hs256Jwt.TryVerify(jwt, DocSecret, out var payload));
        Assert.Equal(string.Empty, payload);
    }

    private static string Encode(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));
}
