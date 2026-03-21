using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMediaBot.Configuration;
using SocialMediaBot.Helpers;

namespace SocialMediaBot.Services;

public class TwitterService(
    IHttpClientFactory httpClientFactory,
    IOptions<TwitterOptions> options,
    ILogger<TwitterService> logger) : ITwitterService
{
    private const int MaxTweetLength = 280;
    private const string TweetUrl = "https://api.twitter.com/2/tweets";
    private const string MediaUploadUrl = "https://upload.twitter.com/1.1/media/upload.json";

    public async Task PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default)
    {
        if (text.Length > MaxTweetLength)
            throw new InvalidOperationException($"Tweet exceeds {MaxTweetLength} characters ({text.Length})");

        var opts = options.Value;
        var client = httpClientFactory.CreateClient("Twitter");

        string? mediaId = null;
        if (photoData is not null)
            mediaId = await UploadMediaAsync(client, opts, photoData, mimeType ?? "image/jpeg", ct);

        var authHeader = OAuth1Helper.GenerateAuthorizationHeader(
            "POST", TweetUrl,
            opts.ApiKey, opts.ApiKeySecret,
            opts.AccessToken, opts.AccessTokenSecret);

        var tweetBody = mediaId is not null
            ? (object)new TweetWithMediaRequest { Text = text, Media = new TweetMedia { MediaIds = [mediaId] } }
            : new TweetRequest { Text = text };

        using var request = new HttpRequestMessage(HttpMethod.Post, TweetUrl);
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        request.Content = JsonContent.Create(tweetBody);

        logger.LogDebug("Posting tweet: {TextLength} chars, hasPhoto={HasPhoto}", text.Length, mediaId is not null);

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Twitter API error {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Twitter API returned {response.StatusCode}: {errorBody}");
        }

        logger.LogInformation("Tweet posted successfully");
    }

    private async Task<string> UploadMediaAsync(
        HttpClient client, TwitterOptions opts, byte[] photoData, string mimeType, CancellationToken ct)
    {
        // OAuth header for multipart upload — body params not included in signature
        var authHeader = OAuth1Helper.GenerateAuthorizationHeader(
            "POST", MediaUploadUrl,
            opts.ApiKey, opts.ApiKeySecret,
            opts.AccessToken, opts.AccessTokenSecret);

        using var content = new MultipartFormDataContent();
        var imageContent = new ByteArrayContent(photoData);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
        content.Add(imageContent, "media", "photo");

        using var request = new HttpRequestMessage(HttpMethod.Post, MediaUploadUrl);
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);
        request.Content = content;

        var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Twitter media upload failed {StatusCode}: {Error}", response.StatusCode, errorBody);
            throw new HttpRequestException($"Twitter media upload failed: {response.StatusCode}: {errorBody}");
        }

        var result = await response.Content.ReadFromJsonAsync<MediaUploadResponse>(ct);
        var mediaId = result?.MediaIdString
            ?? throw new InvalidOperationException("Twitter media upload returned no media_id");

        logger.LogInformation("Twitter media uploaded, id={MediaId}", mediaId);
        return mediaId;
    }
}

file class TweetRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

file class TweetWithMediaRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("media")]
    public required TweetMedia Media { get; set; }
}

file class TweetMedia
{
    [JsonPropertyName("media_ids")]
    public required List<string> MediaIds { get; set; }
}

file class MediaUploadResponse
{
    [JsonPropertyName("media_id_string")]
    public string? MediaIdString { get; set; }
}
