# Architecture Research

**Domain:** Personal AI assistant app (single-user) with .NET MAUI client + ASP.NET Core backend
**Researched:** 2026-03-17
**Confidence:** MEDIUM-HIGH

## Standard Architecture

### System Overview

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Client Layer (.NET MAUI)                           │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────────────────┐ │
│  │ Views + Shell   │  │ ViewModels(MVVM) │  │ Local Data (SQLite/cache) │ │
│  └────────┬────────┘  └────────┬─────────┘  └──────────────┬────────────┘ │
│           │                    │                            │              │
│           └────────────────────┴──────────────┐             │              │
│                                               │             │              │
│                                  ┌────────────▼──────────┐  │              │
│                                  │ API Client + Auth     │◄─┘              │
│                                  │ (HttpClient/SignalR)  │                 │
│                                  └────────────┬──────────┘                 │
├────────────────────────────────────────────────┼─────────────────────────────┤
│                     Backend API Layer (ASP.NET Core)                        │
├────────────────────────────────────────────────┼─────────────────────────────┤
│  ┌─────────────────┐   ┌──────────────────────▼──────────────────────────┐  │
│  │ Controllers/Hub │──►│ Application Core (Orchestration + Use Cases)    │  │
│  │ (REST/SignalR)  │   │ - conversation/session coordinator               │  │
│  └─────────────────┘   │ - memory retrieval + write policy                │  │
│                        │ - tool execution policy + guardrails             │  │
│                        └──────────────┬───────────────────────────────────┘  │
├───────────────────────────────────────┼───────────────────────────────────────┤
│                         Infrastructure Layer                                 │
├───────────────────────────────────────┼───────────────────────────────────────┤
│     ┌──────────────────┐  ┌────────────────────┐  ┌──────────────────────┐  │
│     │ Relational Store │  │ Vector/Knowledge   │  │ LLM + Tool Adapters  │  │
│     │ (sessions,memory)│  │ Index + embeddings │  │ (providers/plugins)  │  │
│     └──────────────────┘  └────────────────────┘  └──────────────────────┘  │
│                           ┌────────────────────────────┐                      │
│                           │ Background Workers         │                      │
│                           │ (indexing/summarization)  │                      │
│                           └────────────────────────────┘                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| MAUI Presentation | Render chat UI and personal assistant UX, route pages, capture user intent | .NET MAUI Shell + MVVM Views/ViewModels |
| MAUI App Services | Manage network calls, local cache, auth/session token, connectivity state | DI services in `MauiProgram`, typed `HttpClient`, optional SignalR client |
| API Entry | Validate requests, authN/authZ, transport contracts | ASP.NET Core Minimal APIs or Controllers + optional SignalR hub |
| Conversation Orchestrator | Main request pipeline: context assembly, model call, tool loop, response streaming | Application service/use-case layer with interfaces |
| Memory Service | Persist and retrieve personal memory/session context with policies | Domain service + repository abstractions |
| Knowledge Vault Service | Ingest docs/notes and retrieve relevant chunks | Ingestion pipeline + vector search adapter |
| Tool Runtime | Execute user-defined workflows safely, enforce timeout/allow-list | Plugin/tool abstraction + executor + sandbox policy |
| Persistence | Durable store for sessions, memory metadata, tool logs | SQL DB + repositories |
| AI Provider Adapter | Normalize model invocation and function-calling across providers | Adapter interfaces in Core, implementations in Infrastructure |
| Worker Pipeline | Async indexing/summarization/maintenance jobs | ASP.NET Core hosted services + bounded queue |

## Recommended Project Structure

```text
src/
├── ChatAyi.App/                       # .NET MAUI app (Android/Windows)
│   ├── Views/                         # XAML pages
│   ├── ViewModels/                    # Presentation logic (MVVM)
│   ├── Services/                      # API client, local state, navigation
│   ├── Storage/                       # SQLite + secure storage wrappers
│   └── MauiProgram.cs                 # DI composition root for client
├── ChatAyi.Api/                       # ASP.NET Core host
│   ├── Endpoints/                     # REST/Hub endpoints
│   ├── Composition/                   # DI wiring only
│   └── Program.cs                     # API composition root
├── ChatAyi.Core/                      # Pure application/domain logic
│   ├── Conversations/                 # Chat/session use cases
│   ├── Memory/                        # Memory policies and contracts
│   ├── Knowledge/                     # Retrieval and ranking contracts
│   ├── Tools/                         # Tool contracts and execution policies
│   └── Abstractions/                  # Interfaces for infra adapters
├── ChatAyi.Infrastructure/            # External implementations
│   ├── Persistence/                   # EF Core/repositories
│   ├── AiProviders/                   # LLM adapters
│   ├── Retrieval/                     # Embedding/vector implementations
│   └── Tooling/                       # Tool connector implementations
└── ChatAyi.Workers/                   # Optional background worker host
    ├── Indexing/                      # Embedding/index jobs
    └── Summarization/                 # Memory compaction jobs
```

### Structure Rationale

- **`ChatAyi.Core` is dependency-inward only:** protects assistant behavior rules from framework churn.
- **`ChatAyi.Infrastructure` is replaceable:** lets you swap model/vector providers without rewriting orchestration.
- **`ChatAyi.Api` stays thin:** transport concerns only; orchestration belongs in Core.
- **`ChatAyi.App` owns UX and local-first resilience:** MAUI caches and secure settings reduce backend coupling.

## Architectural Patterns

### Pattern 1: Clean Architecture (Monolith-first)

**What:** Keep one deployable backend process, but separate Core (rules) from Infrastructure (providers/stores) via interfaces.
**When to use:** Greenfield single-user product with evolving requirements and uncertain provider choices.
**Trade-offs:** Slightly more upfront structure, but lower rewrite risk when swapping persistence/LLM integrations.

**Example:**
```csharp
public interface IChatModelGateway
{
    IAsyncEnumerable<TokenChunk> StreamReplyAsync(ChatTurn turn, CancellationToken ct);
}

public sealed class ConversationOrchestrator
{
    private readonly IChatModelGateway _model;
    private readonly IMemoryRepository _memory;

    public ConversationOrchestrator(IChatModelGateway model, IMemoryRepository memory)
    {
        _model = model;
        _memory = memory;
    }
}
```

### Pattern 2: Orchestrator-Centric Use Cases

**What:** One orchestrator pipeline per user message composes session context, memory, retrieval, model call, and tool execution.
**When to use:** Assistant behavior must stay consistent across channels/pages and over time.
**Trade-offs:** Central coordinator can grow large; mitigate with sub-pipelines (`ContextBuilder`, `ToolPolicy`, `ResponseStreamer`).

### Pattern 3: Async Side-Effects via Hosted Workers

**What:** Keep API response path focused on chat latency; move indexing, summarization, and maintenance jobs to background hosted services.
**When to use:** Any workload not required for immediate assistant reply.
**Trade-offs:** Eventual consistency in memory/knowledge updates; requires job status visibility.

## Data Flow

### Request Flow (primary chat turn)

```text
[User sends message in MAUI]
    -> [ViewModel validates + appends optimistic local message]
    -> [API Client sends request to ASP.NET Core endpoint]
    -> [Conversation Orchestrator loads session + relevant memory + knowledge]
    -> [Model Gateway requests completion and tool calls]
    -> [Tool Runtime executes allowed tools when requested]
    -> [Token stream/events returned to MAUI]
    -> [Final turn persisted to session + memory write queue]
    -> [Background worker updates embeddings/summaries]
```

### State Management Direction

```text
MAUI UI State (ephemeral) -> MAUI Local Cache (SQLite) -> Backend Session Store (source of truth)
                                                              -> Memory Store (long-term)
                                                              -> Knowledge Index (retrieval)
```

### Key Data Flows

1. **Conversation flow:** MAUI -> API -> Orchestrator -> Model/Tools -> streamed response -> persisted turn.
2. **Memory flow:** Turn persisted -> memory extraction policy -> long-term memory store -> retrieval on future turns.
3. **Knowledge flow:** User adds docs/notes -> ingestion queue -> embedding/index update -> retrieval at inference time.

## Suggested Build Order

1. **Foundation and boundaries first**
   - Create solution skeleton (`App`, `Api`, `Core`, `Infrastructure`) and DI composition roots.
   - Define contracts (`IChatModelGateway`, `IMemoryRepository`, `IToolExecutor`, `IKnowledgeRetriever`) before implementations.
2. **Thin end-to-end vertical slice (chat without tools)**
   - MAUI chat screen + API endpoint + orchestrator + one model adapter.
   - Stream responses to prove transport and latency profile early.
3. **Session persistence and memory basics**
   - Add session schema + simple memory write/read policy.
   - Ensure replay of prior turns and context continuity across app restarts.
4. **Knowledge vault ingestion + retrieval**
   - Add document ingestion pipeline, embeddings, and retrieval integration in context assembly.
5. **Tool runtime and safety controls**
   - Introduce tool execution contracts, allow-listing, timeout/isolation policies, and audit logs.
6. **Background processing and optimization**
   - Add hosted workers for indexing/summarization, retry policies, observability, and compaction.

**Build order implication:** Do not start with advanced tools/knowledge indexing. Lock the core chat orchestration and persistence contracts first, or later features will force expensive refactors across both MAUI and API layers.

## Anti-Patterns

### Anti-Pattern 1: Backend logic inside MAUI ViewModels

**What people do:** Put retrieval/memory/tool orchestration logic in client-side ViewModels.
**Why it's wrong:** Breaks consistency, leaks private logic to client, and makes platform-specific bugs part of core behavior.
**Do this instead:** Keep MAUI responsible for UX/state only; keep assistant decision logic in backend Core.

### Anti-Pattern 2: Direct Infrastructure calls from endpoints

**What people do:** Controllers call EF/LLM SDKs directly.
**Why it's wrong:** Hard to test, hard to swap providers, and high coupling to SDK churn.
**Do this instead:** Endpoints call use-case/orchestrator interfaces; infrastructure remains behind adapters.

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| LLM provider(s) | Adapter interface in Core + implementation in Infrastructure | Keep provider-specific payloads out of Core contracts |
| Embedding/vector engine | Retrieval abstraction (`IKnowledgeRetriever`) | Start with one provider; preserve swap path |
| Local secure secrets | MAUI `SecureStorage` for local sensitive values | Store only small secrets; avoid large payloads |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| MAUI App -> API | HTTP + optional SignalR stream | Keep contract DTOs stable and versioned |
| API Endpoints -> Core | In-process interface calls | No direct infrastructure usage in endpoint handlers |
| Core -> Infrastructure | Interfaces + DI | Composition root wires implementation at runtime |
| API -> Workers | Queue/message abstraction | Async tasks for non-latency-critical work |

## Sources

- https://learn.microsoft.com/en-us/dotnet/architecture/maui/ (updated 2024-06-27, enterprise MAUI architecture patterns)
- https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/dependency-injection?view=net-maui-10.0 (updated 2024-12-18, MAUI DI/lifetime guidance)
- https://learn.microsoft.com/en-us/dotnet/maui/data-cloud/database-sqlite?view=net-maui-10.0 (updated 2025-07-31, local SQLite patterns)
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0 (updated 2024-12-18, secure local key/value constraints)
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0 (updated 2026-03-10, DI and composition-root guidance)
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0 (updated 2025-08-28, background processing patterns)
- https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction?view=aspnetcore-10.0 (updated 2025-07-30, real-time transport and fallback model)
- https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines (updated 2025-10-28, client communication and lifetime guidance)
- https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures (updated 2024-01-03, clean architecture layering principles)

---
*Architecture research for: ChatAyi personal AI assistant*
*Researched: 2026-03-17*
