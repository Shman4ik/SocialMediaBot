namespace SocialMediaBot.Features.Telegram;

public class TelegramOptions
{
    public const string SectionName = "Telegram";

    public required string BotToken { get; set; }
    public long[] AllowedChatIds { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
