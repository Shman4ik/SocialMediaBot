namespace SocialMediaBot.Features.Admin;

public class AppState
{
    private volatile bool _isDryRun;
    private volatile bool _isBotRunning = true;
    private CancellationTokenSource _botCts = new();

    public bool IsDryRun
    {
        get => _isDryRun;
        set => _isDryRun = value;
    }

    public bool IsBotRunning => _isBotRunning;

    /// <summary>
    /// Cancellation token that fires when the bot should stop polling.
    /// TelegramBotService links its inner CTS to this token.
    /// </summary>
    public CancellationToken BotStopToken => _botCts.Token;

    public void StartBot()
    {
        // Replace the CTS first so the new token is not yet cancelled
        _botCts = new CancellationTokenSource();
        _isBotRunning = true;
    }

    public void StopBot()
    {
        _isBotRunning = false;
        _botCts.Cancel();
    }

    public void ToggleBot()
    {
        if (_isBotRunning) StopBot();
        else StartBot();
    }
}
