# ChatAyi

ChatAyi is a personal AI chat project that combines:

- A .NET MAUI client app (`ChatAyi/`) for Android and Windows
- A minimal ASP.NET Core API backend (`ChatAyi.Api/`) for chat proxy, memory, and sessions
- A React web client prototype (`nvidia-chat-app/`)

The project is designed for multi-provider chat (Cerebras, NVIDIA Integrate, Inception), local memory/session persistence, and streaming responses.

## Repository Structure

- `ChatAyi/` - .NET MAUI app (main mobile/desktop client)
- `ChatAyi.Api/` - ASP.NET Core backend API
- `nvidia-chat-app/` - Vite + React web prototype
- `ChatAyi.sln` - Visual Studio solution (MAUI + API)

## Main Features

- Multi-provider chat in MAUI app (Cerebras, NVIDIA Integrate, Inception)
- Streaming response handling (SSE-style)
- Local session storage and lightweight memory store in the MAUI app
- Backend endpoints for:
  - chat completion proxy
  - memory search/append/reload
  - model listing
  - transcript-based memory extraction (`/remember`)

## Requirements

- .NET SDK 8.0+
- .NET MAUI workload installed
- Visual Studio 2022 (recommended for MAUI)
- Node.js 18+ (for `nvidia-chat-app`)

## Quick Start

### 1) Build Solution

From repository root:

```bash
dotnet build ChatAyi.sln
```

### 2) Run API Backend (optional but recommended)

```bash
dotnet run --project ChatAyi.Api
```

Default API contains health and utility endpoints at root and `/api/*`.

### 3) Run MAUI App

Use Visual Studio to run `ChatAyi` on Android emulator/device or Windows target.

CLI examples:

```bash
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-android
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-windows10.0.19041.0
```

### 4) Run Web Prototype (optional)

```bash
cd nvidia-chat-app
npm install
npm run dev
```

## Configuration

### Backend (`ChatAyi.Api`)

Set environment variables before running API:

- `CEREBRAS_API_KEY` (required for upstream chat calls)
- `CEREBRAS_API_URL` (optional)
- `CEREBRAS_MODEL` (optional)

`appsettings.json` also includes defaults for API URL/model and workspace/memory settings.

### MAUI App (`ChatAyi`)

API keys are stored on-device using `SecureStorage`:

- `CEREBRAS_API_KEY`
- `NVIDIA_API_KEY`
- `INCEPTION_API_KEY`

Provider/model/user preferences are stored with `Preferences`.

## API Endpoints (ChatAyi.Api)

- `GET /` - health + service metadata
- `POST /api/chat/completions` - streaming chat proxy
- `GET /api/models` - list upstream models
- `GET /api/memory/search?q=...` - search memory index
- `POST /api/memory/append` - append memory text
- `POST /api/memory/reload` - reload memory index
- `POST /api/memory/remember` - extract memory entries from recent transcript

## Comprehensive Update Notes (Latest)

### Stability and startup fixes

- Fixed startup flow that caused app to appear stuck on splash screen.
- `AppShell` route setup was corrected and shell content is explicitly routed.
- App resource initialization was hardened to avoid fatal startup parse failures.

### App resource loading improvements

- `App.xaml` now loads base resources safely (`Colors.xaml`) at bootstrap.
- `Styles.xaml` loading moved into guarded runtime load path (`try/catch`) in `App.xaml.cs`.
- Added debug logs around initialization and style loading to speed diagnosis.

### Theme/color alignment

- Android `Platforms/Android/Resources/values/colors.xml` was aligned with current theme palette.
- Visual consistency between MAUI resources and Android platform colors was improved.

### Git/project hygiene updates

- Added root `.gitignore` for MAUI/.NET and Node artifacts.
- Added ignores for alternate build folders and local runtime/session data.
- Ignored accidental Windows reserved filename artifact (`nul`).
- Sanitized `nvidia-chat-app/server/.env.example` so no real key is committed.

### Current architecture snapshot

- Solution includes MAUI client + API backend.
- Repository also contains standalone React web client prototype.
- MAUI app includes:
  - Get Started page and routed chat page
  - provider/model selection
  - streaming rendering pipeline
  - local memory/session + retrieval helpers

## Troubleshooting

### App stuck on splash screen

Check Visual Studio Output for XAML parse errors around `InitializeComponent()`.

If error references `App.xaml` resource loading:

- verify `Colors.xaml` is loaded first
- verify `Styles.xaml` has valid keys/types
- inspect debug logs added in `App.xaml.cs`

### Missing API key errors

- MAUI: re-enter key in app settings (stored in `SecureStorage`)
- API: ensure `CEREBRAS_API_KEY` is set in environment

## Notes

- This is a personal/private project repository.
- Do not commit real API keys or secret values.
