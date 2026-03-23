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
- Local profile/persona + explicit personal memory (`/memory`)
- `/search` command with hybrid web search + grounded answer template
- `/browse` command with Jina Reader primary fetch and concise Indonesian output

## Runtime Behavior (Latest)

- Voice contract is unified across normal chat, `/search`, and `/browse`:
  - Indonesian casual style with consistent `gua/lu`
  - avoid mixed style (`kamu/Anda/netral`) unless user explicitly requests
- Context contamination hardening:
  - command turns are excluded from active context snapshot for normal chat, `/search`, and `/browse`
  - UI history remains intact, but prompt context is filtered
- Memory boundary is explicit-only:
  - `/remember` is disabled
  - memory writes use explicit flows (`/memory add|update|delete` or explicit natural save intent)

## Search and Browse Pipeline (Latest)

- `/search` uses evidence-based flow:
  1. intent classification
  2. candidate retrieval
  3. page fetch
  4. passage extraction
  5. grounding bundle composition
  6. strict answer composition
- `/search` provider flow:
  1. SearXNG (primary structured search)
  2. Jina Search (conditional booster only when result health is weak)
  3. GitHub search (conditional fallback for code/docs intent when still weak)
  4. Wikipedia (last fallback only if non-wiki coverage is still weak)
  5. DDG (not in normal path; emergency fallback only when everything else is empty)
- Search hardening:
  - target healthy result set: 4-6 items, >=3 unique domains, and >=2 non-wiki items
  - domain diversity, duplicate filtering, low-quality URL filtering
  - evidence passages are prioritized over raw source list
  - non-wiki evidence is attempted first
  - DDG is only used as emergency fallback when earlier providers return empty
- `/browse` fetch flow:
  1. Jina Reader (`r.jina.ai`) primary
  2. direct HTTP fallback
  - blocked/noisy pages fail honestly (no fake success)
  - output forced concise, Indonesian, and paraphrased when source is English

## Search Provider Configuration

`/search` uses SearXNG as configurable provider in the hybrid stack.

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

## Commands

- `/search <query>` - web-grounded answer with strict output template:
  - `[FAKTA]`
  - `[INFERENSI]`
  - `Sumber:`
- `/browse <url> [question]` - summarize page evidence in Indonesian
- `/memory list|add|update|delete|on|off|status` - explicit personal memory control
- `/remember` - disabled (explicit-only memory policy)

## Debug Visibility

The app now includes lightweight runtime tracing via `Debug.WriteLine` for:

- routing branch (`normal-chat`, `search`, `browse`, etc.)
- search provider raw/filtered result counts
- search filter drop reasons (duplicate domain/url, low-quality source, etc.)
- browse path (`jina` vs `fallback-http`) and rejection reasons
- context filter counts (command turns removed from active prompt context)

## Security Notes

- API keys are stored with `SecureStorage` on device.
- Never commit real API keys or secrets.
