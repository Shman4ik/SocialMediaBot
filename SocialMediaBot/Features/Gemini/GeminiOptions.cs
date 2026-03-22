namespace SocialMediaBot.Features.Gemini;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public required string ApiKey { get; set; }
    public string Model { get; set; } = "gemini-2.5-flash";
    public string SpellcheckPrompt { get; set; } = "";
    public string SpellcheckPromptFile { get; set; } = "";
    public string VariantsPrompt { get; set; } = "";
    public string VariantsPromptFile { get; set; } = "";
    public int VariantsCount { get; set; } = 3;
}
