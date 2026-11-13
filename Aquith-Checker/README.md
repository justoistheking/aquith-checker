# Aquith Checker

Discord-connected Fortnite account cosmetics viewer based on the supplied FortniteChecker project.

## Flow

`/login` -> Epic Games login -> OAuth callback -> bounded worker queue -> Fortnite profile queries -> result URL -> Discord DM.

The application does not ask users for Epic passwords or display authorization codes in Discord. OAuth state values are single-use and expire.

## Setup

1. Install .NET 8 SDK.
2. Put your Discord bot token and public HTTPS URL in `config.json`.
3. Your public URL must be reachable by Epic's browser redirect, for example `https://checker.example.com`.
4. Configure the same callback URL in your Epic OAuth/client settings if your Epic client requires an explicit redirect registration:
   `https://checker.example.com/epic/callback`
5. Run:

```bash
dotnet restore
dotnet run -c Release
```

The bot registers `/login` and `/status` globally.

## Architecture

- Discord.Net sharded gateway client
- bounded `Channel<T>` job queue
- configurable worker pool
- independent async HTTP calls
- per-login CSRF state
- no persisted access/refresh tokens
- health endpoint at `/health`

## Important

The result endpoint is inherited from the supplied project. Replace `ResultBaseUrl` with an endpoint you control if you want the exported account data to remain on your own infrastructure.
