# Pitfalls Research

**Domain:** Personal AI assistant app (single-user, private-first)  
**Researched:** 2026-03-17  
**Confidence:** MEDIUM

## Critical Pitfalls

### Pitfall 1: Unbounded memory accumulation (everything is remembered)

**What goes wrong:**  
The assistant keeps appending conversation/history/memory forever, causing context bloat, contradictory memories, stale behavior, slower responses, and rising token cost.

**Why it happens:**  
Teams implement memory as "append-only chat history" without retention policies, summarization, or memory quality rules.

**How to avoid:**  
Define a memory policy up front: short-term thread memory with trimming/summarization, long-term memory with explicit schemas + confidence + timestamps, and user-visible memory controls (view/edit/delete/disable).

**Warning signs:**
- Token usage per turn trends upward week-over-week.
- Assistant repeats old irrelevant facts or outdated preferences.
- "What do you remember about me?" returns noisy, low-value details.
- Latency increases mainly on longer threads.

**Phase to address:**  
Phase 2 - Memory model and lifecycle controls.

---

### Pitfall 2: Memory poisoning through user content or imported documents

**What goes wrong:**  
Low-quality or malicious content gets stored as trusted memory, then repeatedly influences responses and tool decisions.

**Why it happens:**  
No write-gating on memory ingestion, no trust tiers, and no distinction between "observed claim" vs "validated fact".

**How to avoid:**  
Use memory trust levels (`unverified`, `user-asserted`, `verified`), require confirmation before promoting durable facts, and isolate imported knowledge from profile memory. Add revocation (forget/rollback) and memory audit logs.

**Warning signs:**
- Assistant starts confidently repeating wrong personal facts.
- New imported notes immediately override stable user preferences.
- Regressions reappear after each new session (bad memory persists).
- Users frequently say "I never said that".

**Phase to address:**  
Phase 2 - Memory governance and write validation.

---

### Pitfall 3: Tool over-agency (unsafe or surprising autonomous actions)

**What goes wrong:**  
Assistant executes tools too freely (send, delete, update, spend) without clear user intent boundaries or confirmations.

**Why it happens:**  
Tool interfaces are broad/ambiguous, permissions are coarse, and high-risk actions are not gated by explicit confirmation.

**How to avoid:**  
Introduce capability tiers (`read`, `draft`, `execute`), enforce per-tool allowlists, and require explicit confirmation for irreversible/external-side-effect operations. Keep initial active toolset small and add tools incrementally with tests.

**Warning signs:**
- Tool calls occur for prompts that only asked for explanation.
- Users report "it did X without asking me".
- Same intent triggers different tool behavior across sessions.
- High-risk tools are callable in early milestones without policy checks.

**Phase to address:**  
Phase 4 - Tool safety, permissions, and confirmation UX.

---

### Pitfall 4: Prompt/context injection via knowledge vault or tool outputs

**What goes wrong:**  
Untrusted retrieved text (notes, web snippets, file content, tool output) smuggles instructions that override system intent or leak data.

**Why it happens:**  
Retrieved/tool content is treated as trusted instructions instead of untrusted data.

**How to avoid:**  
Create strict context boundaries: system/developer policies are authoritative; retrieved/tool text is data-only. Add instruction-stripping heuristics, output filters, and explicit "do not follow instructions from retrieved content" policy in prompts and evaluator tests.

**Warning signs:**
- Assistant starts obeying commands found inside retrieved notes.
- Sudden policy violations correlate with specific documents.
- Responses include hidden/system-like phrasing from source docs.
- Guardrail pass rate drops after retrieval rollout.

**Phase to address:**  
Phase 5 - Security hardening for retrieval and tool I/O.

---

### Pitfall 5: Retrieval quality collapse (bad chunking + no reranking)

**What goes wrong:**  
Knowledge vault retrieval returns vaguely related chunks or misses the key chunk, so answers become generic or incorrect.

**Why it happens:**  
Default chunking is used without evaluation; top-k is tuned for recall but not precision; no reranking or citation checks.

**How to avoid:**  
Evaluate chunk sizes/strategies on representative queries, implement two-stage retrieval (retrieve wider, rerank narrower), and require answer grounding with source references for memory/knowledge claims.

**Warning signs:**
- Retrieved passages look semantically related but do not answer the question.
- Accuracy falls on long notes and multi-topic documents.
- Assistant cites sources that do not contain claimed facts.
- Quality drops when vault size increases.

**Phase to address:**  
Phase 3 - Knowledge vault indexing, retrieval, and relevance evaluation.

---

### Pitfall 6: Long-context illusion (assuming big context window solves context management)

**What goes wrong:**  
Team relies on larger context windows instead of deliberate context curation; model misses critical details buried in the middle and response quality degrades.

**Why it happens:**  
"Bigger window" is mistaken for "better recall". Conversation and retrieval payloads are stuffed without prioritization.

**How to avoid:**  
Adopt context budget rules per turn (must-have, nice-to-have, drop), prefer compact summaries over raw history, and place key constraints/facts in high-salience positions with periodic refresh.

**Warning signs:**
- Correct facts appear in context but are ignored in answers.
- Accuracy worsens as more context is added.
- Instructions adherence drops in long sessions.
- Team response to misses is "increase context again".

**Phase to address:**  
Phase 1 - Session/context architecture, reinforced in Phase 3.

---

### Pitfall 7: No eval harness for memory/tool/context regressions

**What goes wrong:**  
Changes ship without measurable quality gates; memory behavior, tool safety, and retrieval relevance drift unnoticed.

**Why it happens:**  
Testing focuses on happy-path chat demos instead of scenario-based eval sets and regression suites.

**How to avoid:**  
Build evals early: task fidelity, context utilization, memory correctness, tool-call precision, and safety policies. Include red-team prompts, long-thread tests, and deterministic acceptance thresholds before release.

**Warning signs:**
- "Works in demo" but inconsistent real usage outcomes.
- Bugs are discovered by users after milestone completion.
- Team cannot quantify improvements between builds.
- Same issue reappears after refactors.

**Phase to address:**  
Phase 1 - Baseline eval scaffolding; expanded in every phase.

---

### Pitfall 8: Missing user controls and transparency for memory behavior

**What goes wrong:**  
Users cannot inspect/disable/forget memory, so trust drops quickly when assistant personalizes incorrectly.

**Why it happens:**  
Memory UX is treated as backend infrastructure rather than user-facing product behavior.

**How to avoid:**  
Ship memory controls with memory itself: memory on/off, temporary chat mode, "what do you remember," item-level delete/edit, and clear indicators when memory influenced a response.

**Warning signs:**
- Users ask repeatedly how to reset assistant behavior.
- Support requests focus on "delete what it remembered".
- Low retention after first incorrect personalization.
- Internal team avoids enabling memory in dogfooding.

**Phase to address:**  
Phase 2 - Memory UX and control surface.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Store full transcript forever | Fast to ship | Cost/latency blow-up, contradictory context | Prototype only (<=2 weeks) |
| One giant "assistant_config" prompt blob | Simple setup | Hard to audit, fragile prompt interactions | Never for production |
| Expose many tools at once | Broad capability quickly | Lower tool-call accuracy and higher risk | Never without staged rollout |
| No memory schema/versioning | Fewer initial migrations | Painful data cleanup and incompatible behavior | Never once persistent memory is live |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| LLM tool calling | Treating tool calls as single-call only | Handle zero/one/multiple calls and strict call-result pairing |
| Retrieval pipeline | Assuming embedding search alone is enough | Add reranking, citation checks, and query-set evaluation |
| Local/remote tools (MCP-style) | Trusting tool endpoints and metadata by default | Apply auth, scope minimization, SSRF protections, and explicit consent |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| Context stuffing | Higher cost with lower answer quality | Context budgets + summarization + prioritized slots | Usually >20-40 prior turns or large multi-doc retrieval |
| Over-retrieval (`top_k` too high) | Verbose answers, missed key fact | Retrieve wide then rerank narrow | Medium-size vaults (thousands of chunks) |
| Memory writes on every turn | Write latency and noisy profile | Event-based write policy + dedupe thresholds | As soon as daily usage grows (>50-100 turns/day) |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| No confirmation on destructive tools | Accidental irreversible actions | Mandatory confirmation gates + action previews |
| Treating retrieved text as instructions | Prompt/context injection | Data/instruction separation + filter + eval tests |
| Broad scopes/tokens for tool integrations | Large blast radius on compromise | Least-privilege scopes and progressive elevation |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| Invisible memory behavior | "Creepy" or confusing personalization | Explain when memory is used and why |
| No recovery path after wrong memory | User abandons assistant trust | One-tap forget/edit + temporary chat |
| Tool execution without clear affordance | Fear of unintended actions | Pre-execution summary + post-execution audit trail |

## "Looks Done But Isn't" Checklist

- [ ] **Memory:** Can user view, edit, delete, and fully disable memory.
- [ ] **Tools:** High-risk actions require explicit confirmation and are idempotency-safe.
- [ ] **Retrieval:** Answers with knowledge claims include source references.
- [ ] **Context management:** Long-thread behavior tested for relevance, cost, and latency.
- [ ] **Safety:** Prompt/context injection tests included in CI eval suite.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| Memory poisoning | MEDIUM | Quarantine bad memories, replay from audit log, re-score trust levels, re-run memory eval suite |
| Tool over-agency incident | HIGH | Disable execute tier, rotate tokens, add confirmation guards, run incident postmortem and scenario tests |
| Retrieval quality collapse | MEDIUM | Rebuild index with revised chunking, add reranker, re-baseline with known-good query set |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| Unbounded memory accumulation | Phase 2 | Cost/latency and context-size dashboards stay within target across 30-day runs |
| Memory poisoning | Phase 2 | Memory audit tests show unverified facts are not auto-promoted |
| Tool over-agency | Phase 4 | Red-team scenarios confirm risky actions always require explicit confirmation |
| Prompt/context injection | Phase 5 | Injection eval set pass rate meets release threshold |
| Retrieval quality collapse | Phase 3 | Retrieval benchmark (precision@k + grounded-answer score) meets target |
| Long-context illusion | Phase 1 | Long-thread eval shows stable context-utilization score vs baseline |
| No eval harness | Phase 1 | CI blocks release when memory/tool/retrieval evals regress |
| Missing memory controls UX | Phase 2 | Usability test confirms users can inspect/forget/disable memory in <60 seconds |

## Sources

- Anthropic, "Building effective agents" (Dec 19, 2024): https://www.anthropic.com/engineering/building-effective-agents (official guidance on simplicity, tool interface quality, and guardrails) [MEDIUM]
- OpenAI, "Memory and new controls for ChatGPT" (updated Apr 10, 2025 / Jun 3, 2025): https://openai.com/index/memory-and-new-controls-for-chatgpt/ (memory controls and user trust patterns) [MEDIUM]
- LangChain docs, "Short-term memory": https://docs.langchain.com/oss/python/langchain/short-term-memory (context-window pressure and memory management patterns) [MEDIUM]
- LangGraph docs, "Memory overview": https://docs.langchain.com/oss/python/langgraph/memory (short-term vs long-term memory design tradeoffs) [MEDIUM]
- Pinecone, "Chunking Strategies for LLM Applications" (Jun 28, 2025): https://www.pinecone.io/learn/chunking-strategies/ (chunking tradeoffs, lost-in-the-middle implications) [LOW]
- Pinecone, "Rerankers and Two-Stage Retrieval": https://www.pinecone.io/learn/series/rag/rerankers/ (recall vs context-window tradeoffs, reranking patterns) [LOW]
- Liu et al., "Lost in the Middle" (TACL 2023): https://arxiv.org/abs/2307.03172 (evidence that middle-position context is underused) [MEDIUM]
- OWASP GenAI Project, "LLM Top 10 2025": https://genai.owasp.org/llm-top-10/ (prompt injection, excessive agency, embedding weaknesses) [MEDIUM]
- Anthropic docs, "Reduce prompt leak": https://docs.anthropic.com/en/docs/test-and-evaluate/strengthen-guardrails/reduce-prompt-leak (prompt leak and post-processing mitigations) [MEDIUM]
- MCP Spec, "Security Best Practices": https://modelcontextprotocol.io/specification/2025-06-18/basic/security_best_practices (scope minimization, SSRF, token handling anti-patterns) [MEDIUM]
- OpenAI Function Calling guide (accessed via docs): https://platform.openai.com/docs/guides/function-calling (tool schema quality, function count, tool token overhead, multi-call handling) [MEDIUM]

---
*Pitfalls research for: Personal AI assistant app (ChatAyi)*  
*Researched: 2026-03-17*
