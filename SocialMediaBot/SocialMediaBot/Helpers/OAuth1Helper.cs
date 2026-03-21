using System.Security.Cryptography;
using System.Text;

namespace SocialMediaBot.Helpers;

public static class OAuth1Helper
{
    public static string GenerateAuthorizationHeader(
        string httpMethod,
        string url,
        string consumerKey,
        string consumerSecret,
        string token,
        string tokenSecret,
        string? requestBody = null)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var parameters = new SortedDictionary<string, string>
        {
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = nonce,
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = timestamp,
            ["oauth_token"] = token,
            ["oauth_version"] = "1.0"
        };

        // Build signature base string
        var parameterString = string.Join("&",
            parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var signatureBaseString = string.Join("&",
            httpMethod.ToUpperInvariant(),
            Uri.EscapeDataString(url),
            Uri.EscapeDataString(parameterString));

        // Create signing key
        var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&{Uri.EscapeDataString(tokenSecret)}";

        // Generate signature
        var keyBytes = Encoding.ASCII.GetBytes(signingKey);
        var dataBytes = Encoding.ASCII.GetBytes(signatureBaseString);
        using var hmac = new HMACSHA1(keyBytes);
        var signatureBytes = hmac.ComputeHash(dataBytes);
        var signature = Convert.ToBase64String(signatureBytes);

        parameters["oauth_signature"] = signature;

        // Build Authorization header
        var headerValue = string.Join(", ",
            parameters.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}=\"{Uri.EscapeDataString(kvp.Value)}\""));

        return $"OAuth {headerValue}";
    }
}
