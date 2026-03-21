namespace SocialMediaBot.Services;

public interface IBlueskyService
{
    Task PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default);
}
