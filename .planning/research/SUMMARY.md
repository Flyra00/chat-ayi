# Project Research Summary

**Project:** ChatAyi
**Domain:** Single-user, private-first personal AI assistant (.NET MAUI + ASP.NET Core)
**Researched:** 2026-03-17
**Confidence:** MEDIUM-HIGH

## Executive Summary

ChatAyi should be built as a private-first personal productivity assistant, not a broad multi-tenant SaaS. The combined research points to a modular monolith architecture with a thin MAUI client and backend-centric orchestration, where conversation state, memory, retrieval, and tool policies are enforced consistently in one ASP.NET Core core pipeline. Experts consistently favor this approach for early reliability, lower operational complexity, and easier safety governance.

The recommended implementation path is to lock foundations early: shared .NET 9 stack, Postgres + pgvector in one transactional boundary, Semantic Kernel-based orchestration, and explicit contracts between Core and Infrastructure before feature expansion. MVP scope should stay focused on five table-stakes capabilities: persistent multi-turn chat, memory with controls, knowledge vault retrieval with citations, safe tool execution with confirmation, and cross-device continuity on Windows + Android.

The key delivery risk is not raw model quality; it is behavior drift from weak context, memory, and tool governance. The highest-impact mitigations are: explicit memory lifecycle policies, trust-tiered memory writes, strict tool confirmation gates, retrieval evaluation with reranking/citations, and an eval harness from Phase 1 that blocks regressions before release.

## Key Findings

### Recommended Stack

`STACK.md` recommends a single-version .NET 9 release train across MAUI, ASP.NET Core, and EF Core to reduce compatibility churn. For data, PostgreSQL 17 + pgvector is the default because it keeps relational and vector workloads in one system, which is simpler and safer than dual-store designs at single-user MVP scale.

**Core technologies:**
- `.NET 9 + C# 13`: one language/runtime across app and backend - fewer integration seams and version drift.
- `.NET MAUI 9`: Windows + Android client target with support runway through initial milestones.
- `ASP.NET Core 9`: API + orchestration host with mature middleware/auth/observability ecosystem.
- `PostgreSQL 17 + pgvector 0.8`: durable system-of-record plus semantic retrieval in one transaction boundary.
- `Semantic Kernel 1.73`: C#-native orchestration/tool abstraction for function-calling workflows.
- `Ollama 0.9+`: local/private inference default with OpenAI-compatible endpoint fallback path.

Critical version rule: keep first-party Microsoft components aligned on major `9.x`; avoid mixed major versions across MAUI/API/EF.

### Expected Features

`FEATURES.md` is clear that launch viability depends on trust-centered personalization and action safety, not breadth. Table stakes are persistent chat, controllable memory, grounded personal knowledge retrieval, safe action execution, and cross-device continuity.

**Must have (table stakes):**
- Persistent multi-turn chat + session continuity.
- Memory with explicit user controls (remember/forget/disable/temporary mode).
- Knowledge vault ingestion + retrieval + citations.
- Tool execution with mandatory confirmation for side effects.
- Windows/Android sync for daily-use continuity.

**Should have (competitive):**
- Proactive briefings/reminders tied to explicit user goals.
- Context profiles (Home/Work/Travel) to reduce memory contamination.
- Explainable memory traces (why this answer used this memory/document).

**Defer (v2+):**
- Broad local-sensitive offline/private pipeline expansion.
- Advanced workflow automation (branching/retries/schedules).
- External connectors/plugin-marketplace style extensibility.

### Architecture Approach

`ARCHITECTURE.md` recommends clean architecture with an orchestrator-centric request pipeline and async workers for non-latency-critical tasks. The core design principle is consistency: all assistant behavior rules live server-side in `Core`, while MAUI handles UX/local cache and the API layer remains thin.

**Major components:**
1. `ChatAyi.App` - MAUI UX, local cache, connectivity/auth state, response streaming UI.
2. `ChatAyi.Api` + `ChatAyi.Core` - request entry and orchestration pipeline (context assembly, model/tool loop, safety policies).
3. `ChatAyi.Infrastructure` + workers - persistence, retrieval/indexing, provider adapters, background indexing/summarization.

### Critical Pitfalls

Top risks from `PITFALLS.md` that must shape planning:

1. **Unbounded memory accumulation** - enforce retention, summarization, and user-editable memory lifecycle from the start.
2. **Memory poisoning** - apply trust tiers (`unverified/user-asserted/verified`) and promotion gates before durable memory writes.
3. **Tool over-agency** - capability tiers, allowlists, and mandatory confirmation for irreversible/external actions.
4. **Retrieval quality collapse** - evaluate chunking, implement two-stage retrieval + reranking, require citations.
5. **No eval harness** - install scenario-based CI evals in Phase 1 (context, memory, tool precision, injection safety).

## Implications for Roadmap

Based on combined research, suggested phase structure:

### Phase 1: Foundation and Vertical Slice
**Rationale:** Every downstream feature depends on stable contracts, context pipeline, and measurable quality gates.
**Delivers:** Solution skeleton (`App/Api/Core/Infrastructure`), thin chat E2E slice, streaming response path, baseline eval harness.
**Addresses:** Persistent chat/session continuity baseline.
**Avoids:** Long-context illusion and "demo-only" quality drift by adding context budgets + CI eval scaffolding early.

### Phase 2: Memory System and Trust Controls
**Rationale:** Memory is both core value and top trust risk; must be solved before proactive behaviors.
**Delivers:** Memory schema/versioning, write policies, trust tiers, memory CRUD UX, temporary mode, audit trail.
**Addresses:** Memory with explicit controls (P1 feature).
**Avoids:** Unbounded memory growth, memory poisoning, and opaque personalization.

### Phase 3: Knowledge Vault and Retrieval Quality
**Rationale:** Ingestion without relevance/citation quality hurts trust more than no ingestion.
**Delivers:** Document ingestion pipeline, embedding/index jobs, retrieval + reranking, citation-required response policy.
**Addresses:** Personal knowledge vault (P1 feature) and explainability foundations.
**Avoids:** Retrieval quality collapse and context injection exposure from untrusted retrieved content.

### Phase 4: Tool Runtime and Safe Actioning
**Rationale:** Action capability should follow stable memory/retrieval context to reduce unsafe execution.
**Delivers:** Tool contracts, capability tiers, confirmation UX, timeout/isolation policies, idempotency + audit logs.
**Addresses:** Safe tool execution (P1 feature).
**Avoids:** Tool over-agency incidents and inconsistent cross-session tool behavior.

### Phase 5: Cross-Device Hardening and Differentiators
**Rationale:** After core trust/reliability, prioritize continuity polish and selective P2 differentiation.
**Delivers:** Robust Windows/Android sync, resilience/observability hardening, proactive briefings, context profiles.
**Addresses:** Cross-device continuity (P1) plus selected P2 differentiators.
**Avoids:** Premature expansion into plugin marketplaces/multi-user complexity.

### Phase Ordering Rationale

- Dependencies force order: sessions/context -> memory governance -> retrieval quality -> tools -> proactive UX.
- Architecture supports order: stabilize Core contracts first, then add Infrastructure adapters behind interfaces.
- Pitfall prevention is staged: each phase closes the highest-likelihood failure mode before layering new complexity.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 3:** Retrieval evaluation design (chunking benchmarks, reranker selection, grounding metrics).
- **Phase 4:** Tool safety model details (confirmation UX patterns, permission granularity, incident response).
- **Phase 5:** Cross-device sync conflict resolution and offline reconciliation edge cases.

Phases with standard patterns (skip research-phase):
- **Phase 1:** Clean architecture skeleton + MAUI/API vertical slice are well-documented in official Microsoft guidance.
- **Phase 2 (core mechanics):** Memory CRUD/control surface patterns are now established across major assistant products.

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | HIGH | Strong official-source coverage and clear version alignment strategy. |
| Features | MEDIUM | Good market-signal evidence, but competitor docs are partially product-marketing level. |
| Architecture | HIGH | Mature, well-documented patterns with clear applicability to this scope. |
| Pitfalls | MEDIUM | Strong practical guidance, but several retrieval/tooling sources are community/vendor guidance. |

**Overall confidence:** MEDIUM-HIGH

### Gaps to Address

- **Model strategy baseline:** Select concrete default model(s) and fallback routing thresholds during implementation planning.
- **Retrieval acceptance thresholds:** Define numeric targets (precision@k, grounded-answer score, latency SLOs) before Phase 3 starts.
- **Sync conflict policy:** Specify deterministic merge rules for cross-device session/memory updates before Phase 5.
- **Security validation depth:** Convert injection/tool safety guidance into explicit release-blocking eval criteria.

## Sources

### Primary (HIGH confidence)
- Microsoft .NET/MAUI/ASP.NET Core/EF Core official docs and support policies - stack/version alignment, architecture and hosted-worker patterns.
- PostgreSQL official release notes + pgvector official repository - database/vector baseline and compatibility.
- Official MAUI architecture/DI/SQLite/SecureStorage documentation - client layering and local-state guidance.

### Secondary (MEDIUM confidence)
- Semantic Kernel official overview - orchestration positioning in C# ecosystems.
- OpenAI memory/function-calling docs and product release notes - memory controls and tool-calling expectations.
- Anthropic engineering/docs - effective agent/tool guardrail practices.
- OWASP GenAI Top 10 + MCP security best practices - injection, over-agency, and integration security controls.
- Gemini/Claude feature docs - competitor feature baseline signals.

### Tertiary (LOW confidence)
- Pinecone educational articles (chunking/reranking) - useful retrieval heuristics that need project-specific benchmarking.

---
*Research completed: 2026-03-17*
*Ready for roadmap: yes*
