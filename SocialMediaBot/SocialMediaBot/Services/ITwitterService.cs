namespace SocialMediaBot.Services;

public interface ITwitterService
{
    Task PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default);
}
