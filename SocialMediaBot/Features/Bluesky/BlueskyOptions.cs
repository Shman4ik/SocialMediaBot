namespace SocialMediaBot.Features.Bluesky;

public class BlueskyOptions
{
    public const string SectionName = "Bluesky";

    public required string Identifier { get; set; }
    public required string Password { get; set; }
    public string ServiceUrl { get; set; } = "https://bsky.social";
}
