using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMediaBot.Configuration;

namespace SocialMediaBot.Services;

public class BlueskyService(
    IHttpClientFactory httpClientFactory,
    IOptions<BlueskyOptions> options,
    ILogger<BlueskyService> logger) : IBlueskyService
{
    private string? _accessJwt;
    private string? _did;

    public async Task PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default)
    {
        var opts = options.Value;
        var client = httpClientFactory.CreateClient("Bluesky");
        client.BaseAddress = new Uri(opts.ServiceUrl);

        if (_accessJwt is null || _did is null)
            await CreateSessionAsync(client, opts, ct);

        try
        {
            await CreatePostAsync(client, text, photoData, mimeType, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Bluesky session expired, re-authenticating");
            await CreateSessionAsync(client, opts, ct);
            await CreatePostAsync(client, text, photoData, mimeType, ct);
        }
    }

    private async Task CreateSessionAsync(HttpClient client, BlueskyOptions opts, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(
            "/xrpc/com.atproto.server.createSession",
            new { identifier = opts.Identifier, password = opts.Password },
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Bluesky auth failed: {response.StatusCode}: {error}");
        }

        var session = await response.Content.ReadFromJsonAsync<BlueskySessionResponse>(ct);
        _accessJwt = session?.AccessJwt ?? throw new InvalidOperationException("Missing accessJwt");
        _did = session?.Did ?? throw new InvalidOperationException("Missing did");

        logger.LogInformation("Bluesky session created for {Did}", _did);
    }

    private async Task CreatePostAsync(
        HttpClient client, string text, byte[]? photoData, string? mimeType, CancellationToken ct)
    {
        BlueskyEmbed? embed = null;

        if (photoData is not null)
        {
            var blob = await UploadBlobAsync(client, photoData, mimeType ?? "image/jpeg", ct);
            embed = new BlueskyEmbed
            {
                Type = "app.bsky.embed.images",
                Images = [new BlueskyEmbedImage { Alt = "", Image = blob }]
            };
        }

        var record = new BlueskyPostRecord
        {
            Type = "app.bsky.feed.post",
            Text = text,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            Embed = embed
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.createRecord");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessJwt);
        request.Content = JsonContent.Create(new
        {
            repo = _did,
            collection = "app.bsky.feed.post",
            record
        });

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Bluesky post failed {StatusCode}: {Error}", response.StatusCode, error);
            throw new HttpRequestException($"Bluesky post failed: {response.StatusCode}: {error}");
        }

        logger.LogInformation("Bluesky post created successfully");
    }

    private async Task<BlueskyBlob> UploadBlobAsync(
        HttpClient client, byte[] data, string mimeType, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.repo.uploadBlob");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessJwt);
        request.Content = new ByteArrayContent(data);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Bluesky blob upload failed: {response.StatusCode}: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<BlueskyUploadBlobResponse>(ct);
        return result?.Blob ?? throw new InvalidOperationException("Bluesky blob upload returned no blob");
    }
}

// Models — file-local except BlueskyBlob which is used as a return type
file class BlueskySessionResponse
{
    [JsonPropertyName("accessJwt")] public string? AccessJwt { get; set; }
    [JsonPropertyName("did")] public string? Did { get; set; }
}

file class BlueskyPostRecord
{
    [JsonPropertyName("$type")] public required string Type { get; set; }
    [JsonPropertyName("text")] public required string Text { get; set; }
    [JsonPropertyName("createdAt")] public required string CreatedAt { get; set; }
    [JsonPropertyName("embed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueskyEmbed? Embed { get; set; }
}

file class BlueskyEmbed
{
    [JsonPropertyName("$type")] public required string Type { get; set; }
    [JsonPropertyName("images")] public required List<BlueskyEmbedImage> Images { get; set; }
}

file class BlueskyEmbedImage
{
    [JsonPropertyName("alt")] public required string Alt { get; set; }
    [JsonPropertyName("image")] public required BlueskyBlob Image { get; set; }
}

file class BlueskyUploadBlobResponse
{
    [JsonPropertyName("blob")] public BlueskyBlob? Blob { get; set; }
}

// internal so it can be used as return type of private method in non-file-local class
internal class BlueskyBlob
{
    [JsonPropertyName("$type")] public string? Type { get; set; }
    [JsonPropertyName("ref")] public BlueskyBlobRef? Ref { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

internal class BlueskyBlobRef
{
    [JsonPropertyName("$link")] public string? Link { get; set; }
}
