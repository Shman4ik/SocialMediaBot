using Microsoft.Extensions.Configuration;

namespace SocialMediaBot.Services;

public class MessageProcessingService(
    ITwitterService twitterService,
    IBlueskyService blueskyService,
    IConfiguration configuration,
    ILogger<MessageProcessingService> logger) : IMessageProcessingService
{
    public async Task<PostResult> PostAsync(string text, byte[]? photoData = null, string? mimeType = null, CancellationToken ct = default)
    {
        if (configuration.GetValue<bool>("DryRun"))
        {
            logger.LogInformation("[DRY RUN] Would post: {Text}, hasPhoto={HasPhoto}", text, photoData is not null);
            return new PostResult(text, true, true, null, null);
        }

        bool twitterSuccess = false, blueskySuccess = false;
        string? twitterError = null, blueskyError = null;

        var twitterTask = Task.Run(async () =>
        {
            try
            {
                await twitterService.PostAsync(text, photoData, mimeType, ct);
                twitterSuccess = true;
            }
            catch (Exception ex)
            {
                twitterError = ex.Message;
                logger.LogError(ex, "Failed to post to Twitter");
            }
        }, ct);

        var blueskyTask = Task.Run(async () =>
        {
            try
            {
                await blueskyService.PostAsync(text, photoData, mimeType, ct);
                blueskySuccess = true;
            }
            catch (Exception ex)
            {
                blueskyError = ex.Message;
                logger.LogError(ex, "Failed to post to Bluesky");
            }
        }, ct);

        await Task.WhenAll(twitterTask, blueskyTask);

        return new PostResult(text, twitterSuccess, blueskySuccess, twitterError, blueskyError);
    }
}
