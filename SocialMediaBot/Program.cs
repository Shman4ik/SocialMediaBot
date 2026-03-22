using SocialMediaBot.Features.Bluesky;
using SocialMediaBot.Features.Gemini;
using SocialMediaBot.Features.Posting;
using SocialMediaBot.Features.Telegram;
using SocialMediaBot.Features.Twitter;
using SocialMediaBot.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configuration
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<TwitterOptions>(builder.Configuration.GetSection(TwitterOptions.SectionName));
builder.Services.Configure<BlueskyOptions>(builder.Configuration.GetSection(BlueskyOptions.SectionName));
builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.AddSingleton<MinioService>();

// HttpClients
builder.Services.AddHttpClient("Gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
});

builder.Services.AddHttpClient("Twitter", client =>
{
    client.BaseAddress = new Uri("https://api.twitter.com/");
});

builder.Services.AddHttpClient("Bluesky");

// Services
builder.Services.AddSingleton<IGeminiService, GeminiService>();
builder.Services.AddSingleton<ITwitterService, TwitterService>();
builder.Services.AddSingleton<IBlueskyService, BlueskyService>();
builder.Services.AddScoped<IMessageProcessingService, MessageProcessingService>();

// Telegram bot background service
builder.Services.AddHostedService<TelegramBotService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
