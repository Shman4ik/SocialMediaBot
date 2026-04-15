using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SocialMediaBot.Shared.Storage;

namespace SocialMediaBot.Features.Gemini;

public class GeminiService(
    IHttpClientFactory httpClientFactory,
    IOptions<GeminiOptions> options,
    MinioService minioService,
    ILogger<GeminiService> logger) : IGeminiService
{
    private const string FallbackSeparator = "---VARIANT---";

    private string? _spellcheckPrompt;
    private string? _variantsPrompt;

    public async Task<string> FixSpellingAsync(string rawText, CancellationToken ct = default)
    {
        var opts = options.Value;
        var prompt = await GetSpellcheckPromptAsync(opts, ct);

        var corrected = await CallGeminiAsync($"{prompt}\n\n{rawText}", opts, ct);

        if (string.IsNullOrWhiteSpace(corrected))
        {
            logger.LogWarning("Gemini spellcheck returned empty, using original");
            return rawText;
        }

        return RemoveCorrectedTextLabel(corrected);
    }

    private static string RemoveCorrectedTextLabel(string text)
    {
        const string label = "Исправленный текст";
        var idx = text.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return text.Trim();

        var after = idx + label.Length;

        // Skip optional colon and inline whitespace after the label
        while (after < text.Length && text[after] is ':' or ' ' or '\t')
            after++;

        // Skip one newline (handles both \n and \r\n)
        if (after < text.Length && text[after] == '\r') after++;
        if (after < text.Length && text[after] == '\n') after++;

        var result = text[..idx] + text[after..];

        // Collapse three or more consecutive newlines into two (one blank line)
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");

        return result.Trim();
    }

    public async Task<List<string>> GenerateVariantsAsync(string rawText, CancellationToken ct = default)
    {
        var opts = options.Value;

        var promptText = await GetVariantsPromptAsync(opts, ct);

        var fullPrompt = $"{promptText}\n\nТекст: {rawText}";

        logger.LogDebug("Requesting {Count} variants from Gemini", opts.VariantsCount);

        var rawResponse = await CallGeminiAsync(fullPrompt, opts, ct);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            logger.LogWarning("Gemini returned empty response, returning original text as single variant");
            return [rawText];
        }

        var variants = ParseVariants(rawResponse, opts.VariantsCount);

        if (variants.Count == 0)
        {
            logger.LogWarning("Could not parse variants from Gemini response, returning original");
            return [rawText];
        }

        logger.LogInformation("Gemini returned {Count} variants", variants.Count);
        return variants;
    }

    private async Task<string> GetSpellcheckPromptAsync(GeminiOptions opts, CancellationToken ct)
    {
        if (_spellcheckPrompt is not null)
            return _spellcheckPrompt;

        if (!string.IsNullOrWhiteSpace(opts.SpellcheckPromptFile))
        {
            try
            {
                var s3Text = await minioService.GetTextAsync(opts.SpellcheckPromptFile, ct);
                if (!string.IsNullOrWhiteSpace(s3Text))
                {
                    _spellcheckPrompt = s3Text;
                    return _spellcheckPrompt;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load spellcheck prompt from S3 ({File}), falling back", opts.SpellcheckPromptFile);
            }
        }

        _spellcheckPrompt = string.IsNullOrWhiteSpace(opts.SpellcheckPrompt)
            ? "Исправь только орфографию и пунктуацию, не меняй стиль, слова и структуру. Верни только исправленный текст, без пояснений:"
            : opts.SpellcheckPrompt;

        return _spellcheckPrompt;
    }

    private async Task<string> GetVariantsPromptAsync(GeminiOptions opts, CancellationToken ct)
    {
        if (_variantsPrompt is not null)
            return _variantsPrompt.Replace("{VariantsCount}", opts.VariantsCount.ToString());

        if (!string.IsNullOrWhiteSpace(opts.VariantsPromptFile))
        {
            try
            {
                var s3Text = await minioService.GetTextAsync(opts.VariantsPromptFile, ct);
                if (!string.IsNullOrWhiteSpace(s3Text))
                {
                    _variantsPrompt = s3Text;
                    return _variantsPrompt.Replace("{VariantsCount}", opts.VariantsCount.ToString());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load variants prompt from S3 ({File}), falling back", opts.VariantsPromptFile);
            }
        }

        _variantsPrompt = string.IsNullOrWhiteSpace(opts.VariantsPrompt)
            ? BuildDefaultVariantsPrompt(opts.VariantsCount)
            : opts.VariantsPrompt;

        return _variantsPrompt.Replace("{VariantsCount}", opts.VariantsCount.ToString());
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

    public void InvalidatePromptCache()
    {
        _spellcheckPrompt = null;
        _variantsPrompt = null;
        logger.LogInformation("Gemini prompt cache invalidated; prompts will reload from S3 on next use.");
    }

    private List<string> ParseVariants(string rawResponse, int count)
    {
        // Strip markdown code fences (```json ... ``` or ``` ... ```)
        var trimmed = rawResponse.Trim();
        if (trimmed.StartsWith("```"))
        {
            var start = trimmed.IndexOf('\n') + 1;
            var end = trimmed.LastIndexOf("```");
            if (end > start)
                trimmed = trimmed[start..end].Trim();
        }

        // Try JSON array first — most reliable format
        if (trimmed.StartsWith("["))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed is { Count: > 0 })
                {
                    logger.LogDebug("Parsed variants via JSON");
                    return parsed.Where(v => !string.IsNullOrWhiteSpace(v)).Take(count).ToList();
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "JSON parse failed, falling back to separator split");
            }
        }

        // Fallback: separator split
        var variants = rawResponse
            .Split(FallbackSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Take(count)
            .ToList();

        if (variants.Count > 1)
            logger.LogDebug("Parsed variants via separator split");

        return variants;
    }

    private static string BuildDefaultVariantsPrompt(int count) =>
        $"""
        Ты помогаешь готовить твиты на русском языке. Подготовь ровно {count} варианта твита из текста ниже.
        Правила для каждого варианта:
        - Исправь опечатки и грамматические ошибки, сохрани разговорный стиль
        - Максимум 280 символов
        - Добавь 1-2 уместных эмодзи
        - Не добавляй хештеги, не меняй стиль на официальный
        - Каждый вариант должен отличаться зачином или интонацией

        Верни строго JSON-массив строк без каких-либо пояснений, например:
        ["вариант1", "вариант2", "вариант3"]
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
