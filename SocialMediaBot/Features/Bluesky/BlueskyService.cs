using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace SocialMediaBot.Features.Bluesky;

public class BlueskyService(
    IHttpClientFactory httpClientFactory,
    IOptions<BlueskyOptions> options,
    ILogger<BlueskyService> logger) : IBlueskyService
{
    private string? _accessJwt;
    private string? _refreshJwt;
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
            await RefreshOrCreateSessionAsync(client, opts, ct);
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
        _refreshJwt = session?.RefreshJwt;
        _did = session?.Did ?? throw new InvalidOperationException("Missing did");

        logger.LogInformation("Bluesky session created for {Did}", _did);
    }

    private async Task RefreshOrCreateSessionAsync(HttpClient client, BlueskyOptions opts, CancellationToken ct)
    {
        if (_refreshJwt is not null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/xrpc/com.atproto.server.refreshSession");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _refreshJwt);
                var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var session = await response.Content.ReadFromJsonAsync<BlueskySessionResponse>(ct);
                    _accessJwt = session?.AccessJwt ?? throw new InvalidOperationException("Missing accessJwt");
                    _refreshJwt = session?.RefreshJwt;
                    _did = session?.Did ?? _did;
                    logger.LogInformation("Bluesky session refreshed for {Did}", _did);
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bluesky token refresh failed, falling back to full re-authentication");
            }
        }

        await CreateSessionAsync(client, opts, ct);
    }

    private async Task CreatePostAsync(
        HttpClient client, string text, byte[]? photoData, string? mimeType, CancellationToken ct)
    {
        BlueskyEmbed? embed = null;

        if (photoData is not null)
        {
            var blob = await UploadBlobAsync(client, photoData, mimeType ?? "image/jpeg", ct);
            var aspectRatio = ReadImageDimensions(photoData);
            embed = new BlueskyEmbed
            {
                Type = "app.bsky.embed.images",
                Images = [new BlueskyEmbedImage { Alt = "", Image = blob, AspectRatio = aspectRatio }]
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

    private static BlueskyAspectRatio? ReadImageDimensions(byte[] data)
    {
        // PNG: signature 8 bytes, then IHDR: 4 len + 4 "IHDR" + 4 width + 4 height
        if (data.Length >= 24 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            int width  = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            int height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return new BlueskyAspectRatio { Width = width, Height = height };
        }

        // JPEG: scan for SOF markers (0xFF 0xC0/C1/C2) which contain height then width
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 8 < data.Length)
            {
                if (data[i] != 0xFF) break;
                byte marker = data[i + 1];
                int segLen = (data[i + 2] << 8) | data[i + 3];
                if (marker is 0xC0 or 0xC1 or 0xC2)
                {
                    int height = (data[i + 5] << 8) | data[i + 6];
                    int width  = (data[i + 7] << 8) | data[i + 8];
                    return new BlueskyAspectRatio { Width = width, Height = height };
                }
                i += 2 + segLen;
            }
        }

        return null;
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
            // Bluesky returns 400 BadRequest with ExpiredToken when the access token has expired
            if (error.Contains("ExpiredToken"))
                throw new HttpRequestException($"Bluesky blob upload failed: {response.StatusCode}: {error}", null, System.Net.HttpStatusCode.Unauthorized);
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
    [JsonPropertyName("refreshJwt")] public string? RefreshJwt { get; set; }
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
    [JsonPropertyName("aspectRatio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BlueskyAspectRatio? AspectRatio { get; set; }
}

class BlueskyAspectRatio
{
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
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
