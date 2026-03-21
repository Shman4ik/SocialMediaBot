using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMediaBot.Configuration;

namespace SocialMediaBot.Services;

public class GeminiService(
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiOptions> options,
    ILogger<GeminiService> logger) : IGeminiService
{
    private const string Separator = "|||";

    public async Task<string> FixSpellingAsync(string rawText, CancellationToken ct = default)
    {
        var opts = options.Value;
        var prompt = string.IsNullOrWhiteSpace(opts.SpellcheckPrompt)
            ? "Исправь только орфографию и пунктуацию, не меняй стиль, слова и структуру. Верни только исправленный текст, без пояснений:"
            : opts.SpellcheckPrompt;

        var corrected = await CallGeminiAsync($"{prompt}\n\n{rawText}", opts, ct);

        if (string.IsNullOrWhiteSpace(corrected))
        {
            logger.LogWarning("Gemini spellcheck returned empty, using original");
            return rawText;
        }

        return corrected.Trim();
    }

    public async Task<List<string>> GenerateVariantsAsync(string rawText, CancellationToken ct = default)
    {
        var opts = options.Value;

        var promptText = string.IsNullOrWhiteSpace(opts.VariantsPrompt)
            ? BuildDefaultVariantsPrompt(opts.VariantsCount)
            : opts.VariantsPrompt.Replace("{VariantsCount}", opts.VariantsCount.ToString());

        var fullPrompt = $"{promptText}\n\nТекст: {rawText}";

        logger.LogDebug("Requesting {Count} variants from Gemini", opts.VariantsCount);

        var rawResponse = await CallGeminiAsync(fullPrompt, opts, ct);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            logger.LogWarning("Gemini returned empty response, returning original text as single variant");
            return [rawText];
        }

        var variants = rawResponse
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(opts.VariantsCount)
            .ToList();

        if (variants.Count == 0)
        {
            logger.LogWarning("Could not parse variants from Gemini response, returning original");
            return [rawText];
        }

        logger.LogInformation("Gemini returned {Count} variants", variants.Count);
        return variants;
    }

    private async Task<string?> CallGeminiAsync(string prompt, GeminiOptions opts, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("Gemini");
        var requestUrl = $"v1beta/models/{opts.Model}:generateContent?key={opts.ApiKey}";

        var request = new GeminiRequest
        {
            Contents =
            [
                new GeminiContent { Parts = [new GeminiPart { Text = prompt }] }
            ]
        };

        var response = await client.PostAsJsonAsync(requestUrl, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(ct);
        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
    }

    private static string BuildDefaultVariantsPrompt(int count) =>
        $"""
        Ты помогаешь готовить твиты на русском языке. Подготовь ровно {count} варианта твита из текста ниже.
        Правила для каждого варианта:
        - Исправь опечатки и грамматические ошибки, сохрани разговорный стиль
        - Максимум 280 символов
        - Добавь 1-2 уместных эмодзи
        - Не добавляй хештеги, не меняй стиль на официальный

        Верни ровно {count} варианта, разделённых символами |||
        Без нумерации, без пояснений, только тексты через |||
        """;
}

// Gemini API request/response models
file class GeminiRequest
{
    [JsonPropertyName("contents")]
    public required List<GeminiContent> Contents { get; set; }
}

file class GeminiContent
{
    [JsonPropertyName("parts")]
    public required List<GeminiPart> Parts { get; set; }
}

file class GeminiPart
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

file class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }
}

file class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }
}
