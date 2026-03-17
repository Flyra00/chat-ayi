# Stack Research

**Domain:** Private personal AI assistant (single user) with .NET MAUI client + ASP.NET Core backend
**Researched:** 2026-03-17
**Confidence:** MEDIUM-HIGH

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| .NET SDK + C# | `.NET 9.0.x` + C# 13 | Single runtime/language across mobile, desktop, and backend | Keeps MAUI + ASP.NET Core + EF Core aligned on one release train; reduces cross-version bugs and package drift. |
| .NET MAUI | `9.0.x` | Android + Windows client app | MAUI 9 is the stable generation for 2025 projects, and MAUI 9 support runs until May 2026 (good runway for first milestones). |
| ASP.NET Core Web API | `9.0.x` | Assistant backend APIs, orchestration, auth, tools endpoint | Mature hosting/middleware/auth stack with first-class OpenAPI and strong C# ecosystem alignment. |
| PostgreSQL | `17.x` | Primary system-of-record database | Best default for single-user but serious data durability; strong JSON + relational + text capabilities in one engine. |
| pgvector (Postgres extension) | `0.8.x` | Vector similarity search for memory/knowledge retrieval | Lets you keep embeddings and relational data in one DB (simpler than running a second vector database at this stage). |
| Semantic Kernel | `1.73.0` | Agent/tool orchestration in C# | Stable 1.x SDK with plugin/tool abstractions built for C#; easiest path to function-calling workflows in this stack. |
| Ollama (local model runtime) | `0.9.x+` | Local/private model inference endpoint | Strong privacy default for personal assistant apps, with OpenAI-compatible endpoint support for ecosystem compatibility. |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `9.0.x` | EF Core provider for PostgreSQL | Use for all app data access and migrations from ASP.NET Core. |
| `Pgvector.EntityFrameworkCore` | `0.3.0` | EF mapping and query support for vector columns | Use once you add embedding-backed memory or semantic search. |
| `CommunityToolkit.Maui` | `14.0.1` | MAUI helpers (behaviors, converters, UI utilities) | Use to avoid custom boilerplate in MAUI UI interactions and state glue code. |
| `Serilog.AspNetCore` | `9.0.x` | Structured logging for backend | Use from day one for searchable event logs (tool calls, memory writes, model latency). |
| `OpenTelemetry.Extensions.Hosting` | `1.15.0` | Traces/metrics/log correlation | Use when you need root-cause debugging across client request -> API -> model/tool chain. |
| `Microsoft.Extensions.Http.Resilience` | `9.10.x` | Retry/timeout/circuit breaker for outbound calls | Use for model provider calls (Ollama/cloud fallback) and external tool HTTP actions. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| Docker Desktop + Compose | Run PostgreSQL + pgvector and optional Ollama locally | Keep infra reproducible; avoid machine-specific setup drift. |
| `dotnet-ef` | EF Core migrations and schema evolution | Pin to EF Core major (`9.x`) in tool manifest. |
| `scalar` or Swagger UI | API contract inspection and test calls | Use generated OpenAPI from ASP.NET Core during early backend iteration. |

## Installation

```bash
# Backend core
dotnet add package Microsoft.EntityFrameworkCore --version 9.*
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 9.*
dotnet add package Pgvector.EntityFrameworkCore --version 0.3.*
dotnet add package Microsoft.SemanticKernel --version 1.73.*
dotnet add package Serilog.AspNetCore --version 9.*
dotnet add package OpenTelemetry.Extensions.Hosting --version 1.15.*
dotnet add package Microsoft.Extensions.Http.Resilience --version 9.10.*

# MAUI client
dotnet add package CommunityToolkit.Maui --version 14.0.*

# Tools
dotnet tool install dotnet-ef --version 9.*
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|-------------------------|
| PostgreSQL 17 + pgvector | Qdrant or other standalone vector DB | Use only if vector scale/query patterns outgrow Postgres (not typical for single-user MVP). |
| Semantic Kernel 1.x | Hand-rolled orchestration with raw OpenAI/Ollama calls | Use only for extremely small prototypes where tool orchestration is minimal. |
| Ollama local-first | Cloud-only model APIs | Use if device hardware cannot run acceptable local models or you need frontier model quality first. |
| EF Core + Npgsql | Dapper-only data layer | Use if you intentionally optimize for SQL-first micro-optimizations and can accept slower iteration. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|-------------|
| Kubernetes + service mesh for initial release | Operational overhead is disproportionate for single-user assistant scope | Single ASP.NET Core service + Docker Compose deployment |
| Microservices + message bus (Kafka/RabbitMQ) | Adds distributed failure modes and deployment complexity too early | Modular monolith with in-process background workers |
| Dedicated auth SaaS/enterprise SSO stack upfront | Multi-tenant identity complexity is out of scope for personal assistant | Simple local account + device-bound token/session model |
| Separate vector database at MVP | Dual-write and consistency complexity with little user-value gain | PostgreSQL + pgvector in same transaction boundary |

## Stack Patterns by Variant

**If strictly local/private-first (recommended default):**
- Use Ollama as primary inference endpoint and keep embeddings/documents in local PostgreSQL
- Because this minimizes data egress risk and simplifies privacy guarantees

**If hybrid local + cloud model fallback:**
- Keep Semantic Kernel orchestration and add provider routing (local first, cloud fallback per task)
- Because this preserves privacy defaults while handling harder queries with stronger remote models

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|-----------------|-------|
| `.NET 9.0.x` | `ASP.NET Core 9.0.x`, `EF Core 9.0.x`, `MAUI 9.0.x` | Keep all first-party Microsoft stack on same major version. |
| `Npgsql.EntityFrameworkCore.PostgreSQL 9.0.x` | `EF Core 9.0.x`, `PostgreSQL 17.x` | Recommended alignment for .NET 9 generation. |
| `pgvector 0.8.x` | `PostgreSQL 13+` | Official extension supports Postgres 13+; choose 17 for new installs. |
| `CommunityToolkit.Maui 14.0.x` | `MAUI 9/10 era` | Verify exact MAUI minor before locking final package in implementation phase. |

## Confidence Notes

| Area | Confidence | Notes |
|------|------------|-------|
| Runtime/framework alignment | HIGH | Backed by Microsoft support policy and release docs. |
| Database + vector choice | HIGH | Backed by PostgreSQL + pgvector official docs and current ecosystem use. |
| AI orchestration package choice | MEDIUM | Semantic Kernel is mature 1.x, but ecosystem evolves quickly; re-check at implementation start. |
| Package minor versions | MEDIUM | NuGet indexes verified current versions; exact pin should be finalized during bootstrap commit. |

## Sources

- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core - .NET release/support cadence (updated 2026-03-12)
- https://dotnet.microsoft.com/en-us/platform/support/policy/maui - MAUI support lifecycle (updated 2026-03-11)
- https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-9 - MAUI 9 changes and migration guidance
- https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-9.0 - ASP.NET Core 9 features
- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/whatsnew - EF Core 9 status/support details
- https://www.postgresql.org/about/news/postgresql-17-released-2936/ - PostgreSQL 17 release baseline
- https://github.com/pgvector/pgvector - pgvector capabilities and Postgres 13+ support
- https://learn.microsoft.com/en-us/semantic-kernel/overview/ - Semantic Kernel positioning (official)
- https://ollama.com/blog/openai-compatibility - Ollama OpenAI-compatible endpoint support
- https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/ - MAUI Community Toolkit documentation
- https://api.nuget.org/v3-flatcontainer/microsoft.semantickernel/index.json - package version verification
- https://api.nuget.org/v3-flatcontainer/npgsql.entityframeworkcore.postgresql/index.json - package version verification
- https://api.nuget.org/v3-flatcontainer/pgvector.entityframeworkcore/index.json - package version verification
- https://api.nuget.org/v3-flatcontainer/communitytoolkit.maui/index.json - package version verification
- https://api.nuget.org/v3-flatcontainer/serilog.aspnetcore/index.json - package version verification
- https://api.nuget.org/v3-flatcontainer/opentelemetry.extensions.hosting/index.json - package version verification

---
*Stack research for: ChatAyi personal assistant (single-user, private-first)*
*Researched: 2026-03-17*
