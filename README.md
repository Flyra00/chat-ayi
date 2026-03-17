# ChatAyi (MAUI)

This repository now contains only the .NET MAUI ChatAyi app.

## Structure

- `ChatAyi/` - main .NET MAUI project
- `ChatAyi.sln` - Visual Studio solution for MAUI app only

## Requirements

- .NET SDK 8.0+
- .NET MAUI workload installed
- Visual Studio 2022 (recommended)

## Build

From repository root:

```bash
dotnet build ChatAyi.sln
```

Target-specific builds:

```bash
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-android
dotnet build ChatAyi/ChatAyi.csproj -f net8.0-windows10.0.19041.0
```

## Run

- Open `ChatAyi.sln` in Visual Studio.
- Run on Android emulator/device or Windows target.

## Notes

- API keys are stored with `SecureStorage` in the app.
- Do not commit real API keys or private secrets.
