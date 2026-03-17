# ChatAyi

## What This Is

ChatAyi is a personal AI assistant application for a single user, built with a .NET MAUI client and an ASP.NET Core Web API backend. It focuses on Android and Windows, with private personal-first behavior and no social or multi-channel bot integrations. The product direction is inspired by core OpenClaw capabilities but scoped for a simple, maintainable, and incremental personal assistant.

## Core Value

Deliver a stable personal AI assistant that consistently understands user context and personal knowledge to provide useful, private, and actionable assistance.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] Personal assistant identity that stays consistent across sessions
- [ ] Persistent personal memory with retrieval for future conversations
- [ ] Session and context management for coherent multi-turn interactions
- [ ] Tool execution system for user-defined personal workflows
- [ ] Personal knowledge vault with read and search capabilities

### Out of Scope

- Social media integrations — not aligned with personal assistant scope
- Telegram/Discord/WhatsApp integrations — explicitly excluded by product direction
- Multi-user SaaS architecture — project is personal-first single-user
- Enterprise admin dashboard — unnecessary complexity for intended use
- Large multi-feature phase delivery — violates phased, testable delivery principle

## Context

- Project origin: evolve ChatAyi from a basic chat app into a personal AI assistant
- Inspiration source: OpenClaw, limited to relevant core capabilities only
- Delivery principle: one active phase at a time, minimal and testable changes
- Technical direction: clean modular architecture in C# for maintainability and extensibility
- Product behavior: private/personal context and knowledge should be first-class

## Constraints

- **Tech stack**: .NET MAUI frontend + ASP.NET Core backend in C# — mandated stack direction
- **Platform**: Android and Windows first — target runtime environment
- **Architecture**: Clean and modular — required for long-term maintainability
- **Scope control**: Single active phase and no out-of-phase feature work — avoids overbuilding
- **Product boundary**: Personal assistant only, no social/multi-channel bot behavior — protects vision

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Use .NET MAUI + ASP.NET Core as core stack | Matches project mandate and aligns mobile/desktop + API architecture | — Pending |
| Scope to personal-first single-user assistant | Keeps architecture simple and aligned with intended use | — Pending |
| Prioritize five core capabilities (memory, tools, session/context, knowledge vault, persona/profile) | Captures highest-value OpenClaw-inspired features without platform drift | — Pending |
| Enforce phased delivery with minimal per-phase scope | Reduces implementation risk and supports reliable testing | — Pending |
| Exclude social/messaging platform integrations | Explicitly outside product vision and adds unnecessary complexity | — Pending |

---
*Last updated: 2026-03-17 after initialization*
