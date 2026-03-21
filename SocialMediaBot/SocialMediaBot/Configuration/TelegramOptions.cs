namespace SocialMediaBot.Configuration;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public required string BotToken { get; set; }
    public long[] AllowedChatIds { get; set; } = [];
}
