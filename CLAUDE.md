# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Telegram bot that cross-posts messages to X (Twitter) and Bluesky with AI-powered text processing via Google Gemini. Built with ASP.NET Core (.NET 10) and .NET Aspire for local orchestration.

## Commands

**Build:**
```bash
dotnet build
```

**Run locally (with Aspire orchestration):**
```bash
dotnet run --project SocialMediaBot.AppHost
```

**Run the main service directly:**
```bash
dotnet run --project SocialMediaBot
```

**Configure secrets (required for local dev):**
```bash
dotnet user-secrets set "Telegram:BotToken" "..." --project SocialMediaBot
dotnet user-secrets set "Telegram:AllowedChatIds:0" "..." --project SocialMediaBot
dotnet user-secrets set "Gemini:ApiKey" "..." --project SocialMediaBot
dotnet user-secrets set "Twitter:ConsumerKey" "..." --project SocialMediaBot
dotnet user-secrets set "Bluesky:Username" "..." --project SocialMediaBot
dotnet user-secrets set "Minio:Endpoint" "..." --project SocialMediaBot
dotnet user-secrets set "Admin:Password" "..." --project SocialMediaBot
```

There are no automated tests in this project.

## Architecture

### Project Structure

- **`SocialMediaBot/`** — Main service (ASP.NET Core background worker + web admin)
- **`SocialMediaBot.AppHost/`** — .NET Aspire orchestration host for local development
- **`SocialMediaBot.ServiceDefaults/`** — Shared Aspire defaults: OpenTelemetry, resilience, service discovery

### Feature Modules (`SocialMediaBot/Features/`)

| Module | Responsibility |
|--------|---------------|
| `Telegram/` | Long-polling Telegram bot, inline-keyboard state machine |
| `Gemini/` | Google Gemini AI: spellcheck and style variant generation |
| `Twitter/` | OAuth 1.0a auth, Tweet API v2, media upload |
| `Bluesky/` | AT Protocol auth with proactive JWT refresh, image upload |
| `Posting/` | Orchestrates parallel posting to both platforms |
| `Admin/` | Web admin panel: dry-run toggle, bot start/stop, auth |

`Shared/Storage/MinioService` handles S3-compatible object storage for media files.

### Message Flow

```
Telegram message → TelegramBotService (state machine)
                        ↓
              GeminiService (optional: spellcheck or variants)
                        ↓
            MessageProcessingService (parallel)
              ↙                        ↘
    TwitterService                BlueskyService
```

### State Machine (TelegramBotService)

The bot uses in-memory state flags to track conversation context:
- `_awaitingMode` — message received, waiting for user to pick a mode (direct post, spellcheck, variants)
- `_awaitingSpellcheck` — Gemini result shown, waiting for approval or edit
- `_awaitingVariant` — 3 style variants shown, waiting for user selection
- `_awaitingCustom` — user is typing custom replacement text

### Bluesky Token Refresh

`BlueskyService` proactively refreshes the JWT session ~30 seconds before expiry by parsing the `exp` claim from the access token. This avoids mid-request auth failures.

### Configuration

Key settings in `appsettings.json`:
- `DryRun` — when `true`, skips actual posting (useful for testing)
- `Gemini.VariantsCount` — number of style variants to generate (default: 3)
- `Gemini.SpellcheckPromptFile` / `VariantsPromptFile` — paths to prompt text files in `prompts/`
- `Telegram.AllowedChatIds` — whitelist of Telegram chat IDs

### Deployment

CI/CD via `.github/workflows/docker-build-deploy.yml`:
- Triggers on push to `main`
- Builds multi-stage Docker image, tags as `0.1.{run_number}`
- Pushes to `ghcr.io/shman4ik/socialmediabot`
- Deploys to VPS via SSH

The Dockerfile uses `mcr.microsoft.com/dotnet/aspnet:10.0` as the runtime base, publishing the `SocialMediaBot` project in Release configuration. The admin panel is exposed on port 8081 (mapped from internal 8080).
