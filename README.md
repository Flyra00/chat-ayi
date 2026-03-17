# ChatAyi (MAUI)

ChatAyi is a .NET MAUI chat app for Android and Windows.

This repository contains only the MAUI client project.

## Project Layout

- `ChatAyi/` - main MAUI app source code
- `ChatAyi.sln` - Visual Studio solution

## Requirements

- .NET SDK 8.0+
- .NET MAUI workload
- Visual Studio 2022 (recommended)

## Quick Start

Build from repository root:

```bash
dotnet build ChatAyi.sln
```

Run from Visual Studio:

1. Open `ChatAyi.sln`
2. Choose target (`Android` emulator/device or `Windows`)
3. Start debugging

## Key Features

- Multi-provider chat (Cerebras, NVIDIA Integrate, Inception)
- Session-based conversations
- Local profile/persona + memory support
- `/search` command with hybrid web search:
  - primary: SearXNG
  - fill/fallback: DuckDuckGo, GitHub, Wikipedia

## Search Provider Configuration

`/search` uses SearXNG by default.

- Default base URL: `https://searx.be`
- Optional override via environment variable:

```bash
CHATAYI_SEARXNG_BASE_URL=https://your-searx-instance.example
```

SearXNG request format used by app:

```text
/search?q=<query>&format=json
```

## Build Targets

```bash
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-android
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-windows10.0.19041.0
```

## Security Notes

- API keys are stored with `SecureStorage` on device.
- Never commit real API keys or secrets.
