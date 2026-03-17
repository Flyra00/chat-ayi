# New Session Note (ChatAyi)

## Project

- You are building a personal .NET MAUI chat app named **ChatAyi** (`ChatAyi/`) with realtime streaming LLM responses.
- Earlier in the repo you also prototyped a web app (`nvidia-chat-app/`) and an ASP.NET Core proxy backend (`ChatAyi.Api/`).
- Current active direction: **no backend required**; the MAUI app calls providers directly and stores memory locally on the device.

## Current Architecture (On-Device)

### Providers (switchable, both kept)

- **Cerebras** (OpenAI-compatible)
  - Base URL: `https://api.cerebras.ai`
  - Endpoint: `/v1/chat/completions`
- **NVIDIA Integrate** (OpenAI-compatible)
  - Base URL: `https://integrate.api.nvidia.com/v1/`
  - Endpoint: `chat/completions` (relative path; do NOT use leading `/`)

### API Keys

Keys are stored in SecureStorage (separate keys so switching provider does not delete the other):

- Cerebras: `CEREBRAS_API_KEY`
- NVIDIA: `NVIDIA_API_KEY`

### Local Transcripts (Session JSONL)

- Stored under: `FileSystem.AppDataDirectory/sessions/<sessionId>.jsonl`
- Session id is persisted in Preferences key `ChatAyi.SessionId` and reused across runs.

### Local Workspace Memory (OpenClaw-inspired)

Source of truth is local files on device:

- Long-term memory: `FileSystem.AppDataDirectory/workspace/MEMORY.md`
- Daily log: `FileSystem.AppDataDirectory/workspace/memory/YYYY-MM-DD.md`

Retrieval:

- Keyword-based chunking + scoring (no embeddings yet)
- Injects relevant snippets as a `system` message when chatting

## Chat Commands

### /remember [daily|longterm|both] [note...]

- Reads recent local transcript.
- Calls the selected provider/model to extract memory as JSON:
  - Output shape required: `{ "longterm": [...], "daily": [...] }`
  - Instructs model to think internally but output only JSON.
- Filters obvious secrets (keys/tokens) before writing.
- Appends to local memory files.

### /thinking

- `/thinking on`: think internally, do not reveal reasoning
- `/thinking verbose`: output a short Plan then Answer (more tokens)
- `/thinking off`

### /search <query>

Free search with fallback chain:

1) DuckDuckGo Instant Answer
2) GitHub repository search API
3) Wikipedia OpenSearch

Then auto-browses up to 2 top URLs (when possible) to fetch page text and answers with citations.

### /browse <url> [question]

- Fetches a page, strips HTML to text with size limits.
- Sends excerpt to LLM and answers with citations.

## UI/UX State

- Dark theme with green accent, streaming output throttled to avoid Android ANR.
- Copy assistant messages:
  - Swipe action "Copy"
  - Double-tap assistant bubble to copy
- Settings overlay (gear button) toggles:
  - Web tools (/search,/browse)
  - Remember (/remember)
  - Show command tips
  - Thinking + Verbose
  - Structured answers

Structured answers toggle adds formatting constraints:

- Use headings (e.g. "Jawaban", "Poin Penting").
- For web answers, include citations `[1] [2]` and a "Sumber" section.
- Do not invent links/repos/facts; if unsure, ask for a URL or use `/browse`.

## Provider/Model Selection

- Provider is selectable in Settings via Picker.
- Model is editable in Settings and persisted per provider:
  - Cerebras default: `gpt-oss-120b`
  - NVIDIA default: `z-ai/glm5`
- Header subtitle reflects current provider.

## Performance/Crash Fixes Applied

- Streaming updates are throttled (~60ms) to avoid Android ANR.
- SSE parsing respects cancellation.
- SSE parsing skips chunks that cannot contain output text (important for reasoning-heavy streams).
- NVIDIA 404 fix: use relative endpoint `chat/completions` with base URL `.../v1/`.
- NVIDIA speed tweaks:
  - Smaller `max_tokens`
  - Shorter chat history and truncation of long messages/memory context

## Known Limitations / Gotchas

- Some networks/devices fail TLS/DNS to DuckDuckGo and/or certain sites; `/search` may fall back to GitHub/Wikipedia; `/browse` can fail on blocked domains.
- NVIDIA reasoning deltas (`reasoning_content`/`reasoning`) are not displayed; only final `content`/`text` is used.
- On-device keys live on the device (SecureStorage). This is acceptable since the project is personal and not published.

## Key Files

- UI: `ChatAyi/Pages/ChatPage.xaml`, `ChatAyi/Pages/ChatPage.xaml.cs`
- Provider client: `ChatAyi/Services/ChatApiClient.cs`
- Local memory: `ChatAyi/Services/LocalMemoryStore.cs`
- Local sessions: `ChatAyi/Services/LocalSessionStore.cs`
- Search: `ChatAyi/Services/FreeSearchClient.cs`, `ChatAyi/Services/DdgSearchClient.cs`
- Browse: `ChatAyi/Services/BrowseClient.cs`
- DI: `ChatAyi/MauiProgram.cs`
- Styles: `ChatAyi/Resources/Styles/Colors.xaml`, `ChatAyi/Resources/Styles/Styles.xaml`

## Legacy (Not Current)

- `ChatAyi.Api/`: ASP.NET Core proxy backend with server-side memory + sessions + `/models` + `/remember`; used earlier for USB reverse testing.
- `nvidia-chat-app/`: Vite/React prototype.
