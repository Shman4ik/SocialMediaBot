namespace SocialMediaBot.Features.Twitter;

public class TwitterOptions
{
    public const string SectionName = "Twitter";

    public required string ApiKey { get; set; }
    public required string ApiKeySecret { get; set; }
    public required string AccessToken { get; set; }
    public required string AccessTokenSecret { get; set; }
}
