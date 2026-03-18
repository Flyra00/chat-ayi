# Session Note

Last updated: 2026-03-17

## Scope Completed

- Stabilized `/search` grounding quality with hybrid provider flow and non-wiki bias.
- Stabilized `/browse` output to be concise, Indonesian, and less dump-like.
- Hardened context isolation so command branches do not contaminate normal chat.
- Unified ChatAyi voice contract across normal chat, `/search`, and `/browse`.
- Enforced explicit-only memory boundary by disabling `/remember`.
- Added lightweight runtime observability logs for routing/search/browse/context filtering.

## Current Runtime Contracts

- Voice: Indonesian casual with consistent `gua/lu`.
- `/search` output format (strict):
  - `[FAKTA]`
  - `[INFERENSI]`
  - `Sumber:`
- `/browse` output: concise Indonesian summary, paraphrased from source if needed.
- Memory writes: explicit only via `/memory` and explicit save intent.

## Search/Browse Notes

- Search stack order:
  1) Jina Search
  2) SearXNG
  3) DDG
  4) GitHub
  5) Wikipedia
- Browse stack order:
  1) Jina Reader
  2) direct HTTP fallback
- Noisy/blocked content returns honest failure (no fake success).

## Context Isolation Notes

- Active prompt context for normal chat, `/search`, and `/browse` excludes command turns (`/ ...`).
- UI transcript/history is still fully visible to user.

## Quick Verification Checklist

1. Run `/search dotnet maui securestorage`:
   - should not collapse to wiki-only when non-wiki sources are available.
2. Run `/browse <english-url>`:
   - output should remain Indonesian and concise.
3. Run command sequence `/search ...` -> normal chat:
   - normal chat should not inherit command style/template.
4. Build:
   - `dotnet build ChatAyi.sln`
