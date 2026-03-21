namespace SocialMediaBot.Configuration;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    public required string ApiKey { get; set; }
    public string Model { get; set; } = "gemini-2.5-flash";
    public string SpellcheckPrompt { get; set; } = "";
    public string VariantsPrompt { get; set; } = "";
    public int VariantsCount { get; set; } = 3;
}
