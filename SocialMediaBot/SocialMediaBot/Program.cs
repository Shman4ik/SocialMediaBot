using SocialMediaBot.Configuration;
using SocialMediaBot.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Configuration
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<TwitterOptions>(builder.Configuration.GetSection(TwitterOptions.SectionName));
builder.Services.Configure<BlueskyOptions>(builder.Configuration.GetSection(BlueskyOptions.SectionName));

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
