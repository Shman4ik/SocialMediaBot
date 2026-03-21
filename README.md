# SocialMediaBot

A personal Telegram bot that cross-posts messages to **X (Twitter)** and **Bluesky**, with **Google Gemini** AI assistance for text processing.

## Features

- 📨 **Telegram-driven** — send a message or photo to the bot and it handles the rest
- 🔤 **Spellcheck mode** — Gemini fixes typos and punctuation while preserving your writing style
- 🎨 **Style variants mode** — Gemini generates 3 tweet variants in your personal tone for you to choose from
- ✏️ **Custom text** — write your own final version at any step
- 🖼️ **Photo support** — attach an image with or without a caption
- ✅ **Approval flow** — nothing is posted until you explicitly confirm via inline buttons
- 🧪 **Dry Run mode** — test the full flow without actually posting to any social network
- 🔒 **Whitelist** — only allowed Telegram chat IDs can interact with the bot

## Architecture

Built with **.NET 10** and [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/):

```
SocialMediaBot/
├── SocialMediaBot/              # Main service (background worker)
│   ├── Services/
│   │   ├── TelegramBotService   # Long-polling bot, state machine, inline keyboards
│   │   ├── GeminiService        # AI text processing (spellcheck + style variants)
│   │   ├── TwitterService       # Posts to X via OAuth 1.0a
│   │   ├── BlueskyService       # Posts to Bluesky via AT Protocol
│   │   └── MessageProcessingService  # Orchestrates Twitter + Bluesky posting
│   └── Configuration/           # Strongly-typed options classes
├── SocialMediaBot.AppHost/      # Aspire orchestration host (local dev)
└── SocialMediaBot.ServiceDefaults/  # Shared Aspire defaults
```

## User Flow

```
[Send message / photo to Telegram bot]
              ↓
    ┌─────────────────────┐
    │  How to process?    │
    │ [🔤 Fix spelling]   │
    │ [🎨 Style variants] │
    │ [❌ Cancel]         │
    └─────────────────────┘
          ↙           ↘
  Spellcheck         Variants
  ─────────          ────────
  One corrected      3 style options
  version shown      numbered 1–3
        ↓                 ↓
  [✅ Post] [✏️ Write own] [❌ Cancel]
        ↓
  Posted to X + Bluesky simultaneously
```

**Photo without caption:**
```
[Send photo] → [📤 Post without caption] / [✏️ Add caption] / [❌ Cancel]
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### API Keys

| Service | Where to get |
|---------|-------------|
| **Telegram Bot Token** | [@BotFather](https://t.me/BotFather) → `/newbot` |
| **Telegram Chat ID** | Open `https://api.telegram.org/bot<TOKEN>/getUpdates` after sending `/start` |
| **Gemini API Key** | [aistudio.google.com](https://aistudio.google.com) → Get API key |
| **X Consumer Key + Secret** | [developer.x.com](https://developer.x.com) → Your app → Keys and Tokens |
| **X Access Token + Secret** | Same page → Generate Access Token and Secret |
| **Bluesky Identifier** | Your handle, e.g. `yourname.bsky.social` |
| **Bluesky App Password** | bsky.app → Settings → App Passwords → Add App Password |

### Local Setup

```bash
cd SocialMediaBot/SocialMediaBot

dotnet user-secrets set "Telegram:BotToken"              "123456:ABC..."
dotnet user-secrets set "Telegram:AllowedChatIds:0"      "123456789"

dotnet user-secrets set "Gemini:ApiKey"                  "AIza..."

dotnet user-secrets set "Twitter:ApiKey"                 "..."
dotnet user-secrets set "Twitter:ApiKeySecret"           "..."
dotnet user-secrets set "Twitter:AccessToken"            "..."
dotnet user-secrets set "Twitter:AccessTokenSecret"      "..."

dotnet user-secrets set "Bluesky:Identifier"             "yourname.bsky.social"
dotnet user-secrets set "Bluesky:Password"               "xxxx-xxxx-xxxx-xxxx"
```

### Run

```bash
dotnet run --project SocialMediaBot.AppHost
```

### Dry Run

Set `"DryRun": true` in `appsettings.json` (default for development) to test the full flow without posting to any social network. The bot will show `🧪 Dry Run — not sent to social networks`.

## Configuration

All settings live in `appsettings.json`. Secrets are stored via `dotnet user-secrets` locally and GitHub Actions secrets in production.

```json
{
  "DryRun": false,
  "Gemini": {
    "Model": "gemini-3.1-flash-lite-preview",
    "VariantsCount": 3,
    "SpellcheckPrompt": "...",
    "VariantsPrompt": "..."
  },
  "Bluesky": {
    "ServiceUrl": "https://bsky.social"
  }
}
```

## Deployment

The project includes a GitHub Actions workflow (`.github/workflows/docker-build-deploy.yml`) that:

1. Builds a Docker image on every push to `master`
2. Pushes it to GitHub Container Registry (`ghcr.io`)
3. Deploys to a VPS via SSH
