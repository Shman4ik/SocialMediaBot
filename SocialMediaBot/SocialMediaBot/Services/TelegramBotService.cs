using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SocialMediaBot.Configuration;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SocialMediaBot.Services;

public class TelegramBotService(
    IOptions<TelegramOptions> options,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private readonly HashSet<long> _allowedChatIds = new(options.Value.AllowedChatIds);

    // State records
    private record ModeState(string RawText, byte[]? PhotoData, string? MimeType);
    private record SpellcheckState(int MessageId, string CorrectedText, byte[]? PhotoData, string? MimeType);
    private record VariantsState(int MessageId, List<string> Variants, byte[]? PhotoData, string? MimeType);
    private record AwaitingCustomState(int MessageId, byte[]? PhotoData, string? MimeType);

    // chatId -> state
    private readonly ConcurrentDictionary<long, ModeState> _awaitingMode = new();
    private readonly ConcurrentDictionary<long, SpellcheckState> _awaitingSpellcheck = new();
    private readonly ConcurrentDictionary<long, VariantsState> _awaitingVariant = new();
    private readonly ConcurrentDictionary<long, AwaitingCustomState> _awaitingCustom = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var client = new TelegramBotClient(options.Value.BotToken);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        logger.LogInformation("Telegram bot started. DryRun={DryRun}. Allowed: {ChatIds}",
            configuration.GetValue<bool>("DryRun"),
            _allowedChatIds.Count > 0 ? string.Join(", ", _allowedChatIds) : "ALL");

        await client.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        if (update.Message is { } msg && (msg.Text is not null || msg.Photo is not null))
            await HandleMessageAsync(client, msg, ct);
        else if (update.CallbackQuery is { } cb)
            await HandleCallbackAsync(client, cb, ct);
    }

    // ── Incoming message ────────────────────────────────────────────────────

    private async Task HandleMessageAsync(ITelegramBotClient client, Message message, CancellationToken ct)
    {
        var chatId = message.Chat.Id;

        if (_allowedChatIds.Count > 0 && !_allowedChatIds.Contains(chatId))
        {
            await client.SendMessage(chatId, "⛔ У вас нет доступа к этому боту.", cancellationToken: ct);
            return;
        }

        // User is typing custom tweet text
        if (message.Text is { } customText && _awaitingCustom.TryRemove(chatId, out var awaitingCustom))
        {
            await PostAsync(client, chatId, awaitingCustom.MessageId, customText, awaitingCustom.PhotoData, awaitingCustom.MimeType, ct);
            return;
        }

        // Photo without caption → special flow (no Gemini mode needed yet)
        if (message.Photo is { } photos && string.IsNullOrEmpty(message.Caption))
        {
            var photoData = await DownloadPhotoAsync(client, photos, ct);
            var keyboard = new InlineKeyboardMarkup(
            [
                [InlineKeyboardButton.WithCallbackData("📤 Опубликовать без подписи", "v:no_text")],
                [InlineKeyboardButton.WithCallbackData("✏️ Добавить подпись", "add_caption")],
                [InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel")]
            ]);
            var msg = await client.SendMessage(chatId, "🖼 Фото получено. Что делаем?",
                replyMarkup: keyboard, cancellationToken: ct);
            // Store photo for this message
            _awaitingMode[chatId] = new ModeState("", photoData, "image/jpeg");
            // Replace with no-text-capable awaiting variant state
            _awaitingVariant[chatId] = new VariantsState(msg.MessageId, [], photoData, "image/jpeg");
            return;
        }

        // Text or photo+caption → show mode selection
        var text = message.Text ?? message.Caption ?? "";
        byte[]? photo = null;
        string? mime = null;

        if (message.Photo is { } captionedPhotos)
        {
            photo = await DownloadPhotoAsync(client, captionedPhotos, ct);
            mime = "image/jpeg";
        }

        _awaitingMode[chatId] = new ModeState(text, photo, mime);

        var modeKeyboard = new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData("🔤 Исправить орфографию", "mode:spell"),
                InlineKeyboardButton.WithCallbackData("🎨 Варианты стилей", "mode:style")
            ],
            [InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel")]
        ]);

        await client.SendMessage(chatId, "Как обрабатываем?",
            replyMarkup: modeKeyboard, cancellationToken: ct);
    }

    // ── Callbacks ────────────────────────────────────────────────────────────

    private async Task HandleCallbackAsync(ITelegramBotClient client, CallbackQuery callback, CancellationToken ct)
    {
        var chatId = callback.Message!.Chat.Id;
        var messageId = callback.Message.MessageId;

        await client.AnswerCallbackQuery(callback.Id, cancellationToken: ct);

        switch (callback.Data)
        {
            case "cancel":
                ClearAllState(chatId);
                await client.EditMessageText(chatId, messageId, "❌ Отменено.", cancellationToken: ct);
                return;

            case "mode:spell":
                await HandleModeSpellAsync(client, chatId, messageId, ct);
                return;

            case "mode:style":
                await HandleModeStyleAsync(client, chatId, messageId, ct);
                return;

            case "approve":
                await HandleApproveAsync(client, chatId, messageId, ct);
                return;

            case "add_caption":
                // User wants to add caption to a photo-only message
                _awaitingVariant.TryRemove(chatId, out _);
                var photo = _awaitingMode.TryGetValue(chatId, out var ms) ? (ms.PhotoData, ms.MimeType) : (null, null);
                _awaitingMode.TryRemove(chatId, out _);
                _awaitingCustom[chatId] = new AwaitingCustomState(messageId, photo.PhotoData, photo.MimeType);
                await client.EditMessageText(chatId, messageId, "✏️ Напиши подпись к фото:", cancellationToken: ct);
                return;

            case "custom":
                await HandleCustomAsync(client, chatId, messageId, ct);
                return;

            default:
                if (callback.Data?.StartsWith("v:") == true)
                    await HandleVariantSelectAsync(client, chatId, messageId, callback.Data, ct);
                return;
        }
    }

    private async Task HandleModeSpellAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken ct)
    {
        if (!_awaitingMode.TryRemove(chatId, out var state))
        {
            await client.EditMessageText(chatId, messageId, "⚠️ Состояние устарело, отправь сообщение снова.", cancellationToken: ct);
            return;
        }

        await client.EditMessageText(chatId, messageId, "🔤 Исправляю орфографию...", cancellationToken: ct);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var gemini = scope.ServiceProvider.GetRequiredService<IGeminiService>();
            var corrected = await gemini.FixSpellingAsync(state.RawText, ct);

            _awaitingSpellcheck[chatId] = new SpellcheckState(messageId, corrected, state.PhotoData, state.MimeType);

            var prefix = state.PhotoData is not null ? "🖼 " : "";
            var keyboard = new InlineKeyboardMarkup(
            [
                [InlineKeyboardButton.WithCallbackData("✅ Опубликовать", "approve")],
                [InlineKeyboardButton.WithCallbackData("✏️ Написать свой", "custom")],
                [InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel")]
            ]);

            await client.EditMessageText(
                chatId, messageId,
                $"{prefix}<b>Исправленный текст:</b>\n\n{Encode(corrected)}",
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Spellcheck failed for {ChatId}", chatId);
            await client.EditMessageText(chatId, messageId, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task HandleModeStyleAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken ct)
    {
        if (!_awaitingMode.TryRemove(chatId, out var state))
        {
            await client.EditMessageText(chatId, messageId, "⚠️ Состояние устарело, отправь сообщение снова.", cancellationToken: ct);
            return;
        }

        await client.EditMessageText(chatId, messageId, "🎨 Генерирую варианты...", cancellationToken: ct);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var gemini = scope.ServiceProvider.GetRequiredService<IGeminiService>();
            var variants = await gemini.GenerateVariantsAsync(state.RawText, ct);

            _awaitingVariant[chatId] = new VariantsState(messageId, variants, state.PhotoData, state.MimeType);

            var prefix = state.PhotoData is not null ? "🖼 " : "";
            var lines = new List<string> { $"{prefix}Выбери вариант:\n" };
            for (var i = 0; i < variants.Count; i++)
                lines.Add($"<b>{i + 1}.</b> {Encode(variants[i])}");

            var variantButtons = variants
                .Select((_, i) => InlineKeyboardButton.WithCallbackData($"{i + 1}", $"v:{i}"))
                .ToList();

            var keyboard = new InlineKeyboardMarkup(
            [
                variantButtons,
                new[] { InlineKeyboardButton.WithCallbackData("✏️ Написать свой", "custom") },
                new[] { InlineKeyboardButton.WithCallbackData("❌ Отмена", "cancel") }
            ]);

            await client.EditMessageText(
                chatId, messageId,
                string.Join("\n\n", lines),
                parseMode: ParseMode.Html,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Variants generation failed for {ChatId}", chatId);
            await client.EditMessageText(chatId, messageId, $"❌ Ошибка: {ex.Message}", cancellationToken: ct);
        }
    }

    private async Task HandleApproveAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken ct)
    {
        if (!_awaitingSpellcheck.TryRemove(chatId, out var state))
        {
            await client.SendMessage(chatId, "⚠️ Состояние устарело, отправь сообщение снова.", cancellationToken: ct);
            return;
        }
        await PostAsync(client, chatId, messageId, state.CorrectedText, state.PhotoData, state.MimeType, ct);
    }

    private async Task HandleCustomAsync(ITelegramBotClient client, long chatId, int messageId, CancellationToken ct)
    {
        // Custom text can come from either spellcheck or variants state
        byte[]? photo = null;
        string? mime = null;

        if (_awaitingSpellcheck.TryRemove(chatId, out var sp))
            (photo, mime) = (sp.PhotoData, sp.MimeType);
        else if (_awaitingVariant.TryRemove(chatId, out var vs))
            (photo, mime) = (vs.PhotoData, vs.MimeType);

        _awaitingCustom[chatId] = new AwaitingCustomState(messageId, photo, mime);
        await client.EditMessageText(chatId, messageId,
            photo is not null ? "✏️ Напиши подпись к фото:" : "✏️ Напиши свой вариант твита:",
            cancellationToken: ct);
    }

    private async Task HandleVariantSelectAsync(
        ITelegramBotClient client, long chatId, int messageId, string data, CancellationToken ct)
    {
        if (!_awaitingVariant.TryGetValue(chatId, out var state))
        {
            await client.SendMessage(chatId, "⚠️ Варианты устарели, отправь сообщение снова.", cancellationToken: ct);
            return;
        }

        string text;
        if (data == "v:no_text")
        {
            text = "";
        }
        else if (int.TryParse(data[2..], out var idx) && idx < state.Variants.Count)
        {
            text = state.Variants[idx];
        }
        else return;

        _awaitingVariant.TryRemove(chatId, out _);
        await PostAsync(client, chatId, messageId, text, state.PhotoData, state.MimeType, ct);
    }

    // ── Posting ──────────────────────────────────────────────────────────────

    private async Task PostAsync(
        ITelegramBotClient client, long chatId, int messageId,
        string text, byte[]? photoData, string? mimeType, CancellationToken ct)
    {
        var preview = string.IsNullOrEmpty(text) ? "📤 Публикую фото..." : $"📤 Публикую:\n\n<i>{Encode(text)}</i>";
        await client.EditMessageText(chatId, messageId, preview, parseMode: ParseMode.Html, cancellationToken: ct);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var processing = scope.ServiceProvider.GetRequiredService<IMessageProcessingService>();
            var result = await processing.PostAsync(text, photoData, mimeType, ct);

            var body = string.IsNullOrEmpty(text) ? "🖼 Фото\n\n" : $"<i>{Encode(text)}</i>\n\n";
            await client.EditMessageText(chatId, messageId,
                body + FormatPostResult(result),
                parseMode: ParseMode.Html, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error posting for {ChatId}", chatId);
            await client.SendMessage(chatId, $"❌ Ошибка при публикации: {ex.Message}", cancellationToken: ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearAllState(long chatId)
    {
        _awaitingMode.TryRemove(chatId, out _);
        _awaitingSpellcheck.TryRemove(chatId, out _);
        _awaitingVariant.TryRemove(chatId, out _);
        _awaitingCustom.TryRemove(chatId, out _);
    }

    private static async Task<byte[]> DownloadPhotoAsync(
        ITelegramBotClient client, PhotoSize[] photos, CancellationToken ct)
    {
        var largest = photos.MaxBy(p => p.FileSize) ?? photos.Last();
        using var ms = new MemoryStream();
        await client.GetInfoAndDownloadFile(largest.FileId, ms, ct);
        return ms.ToArray();
    }

    private string FormatPostResult(PostResult result)
    {
        if (configuration.GetValue<bool>("DryRun"))
            return "🧪 <b>Dry Run</b> — в соцсети не отправлено";

        return string.Join("\n",
            result.TwitterSuccess ? "✅ Twitter: опубликовано" : $"❌ Twitter: {Encode(result.TwitterError ?? "ошибка")}",
            result.BlueskySuccess ? "✅ Bluesky: опубликовано" : $"❌ Bluesky: {Encode(result.BlueskyError ?? "ошибка")}");
    }

    private static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);

    private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Telegram bot polling error");
        return Task.CompletedTask;
    }
}
