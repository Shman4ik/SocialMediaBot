namespace SocialMediaBot.Features.Gemini;

public interface IGeminiService
{
    Task<string> FixSpellingAsync(string rawText, CancellationToken ct = default);
    Task<List<string>> GenerateVariantsAsync(string rawText, CancellationToken ct = default);
    void InvalidatePromptCache();
}
