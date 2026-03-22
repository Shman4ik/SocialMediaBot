using Microsoft.AspNetCore.Authentication.Cookies;
using SocialMediaBot.Features.Admin;
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
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services.AddSingleton<MinioService>();

// Runtime state (seeded from config)
var appState = new AppState { IsDryRun = builder.Configuration.GetValue<bool>("DryRun") };
var telegramEnabled = builder.Configuration.GetSection(TelegramOptions.SectionName).GetValue<bool>("Enabled", true);
if (!telegramEnabled) appState.StopBot();
builder.Services.AddSingleton(appState);

// Authentication / Authorization
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/admin/login";
        options.AccessDeniedPath = "/admin/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();
app.MapAdminEndpoints();

app.Run();
