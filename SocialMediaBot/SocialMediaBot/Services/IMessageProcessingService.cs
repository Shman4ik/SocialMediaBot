namespace SocialMediaBot.Services;

public record PostResult(
    string Text,
    bool TwitterSuccess,
    bool BlueskySuccess,
    string? TwitterError,
    string? BlueskyError);

public interface IMessageProcessingService
{
    Task<PostResult> PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default);
}
