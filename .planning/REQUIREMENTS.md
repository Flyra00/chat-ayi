# Requirements: ChatAyi

**Defined:** 2026-03-17
**Core Value:** Deliver a stable personal AI assistant that consistently understands user context and personal knowledge to provide useful, private, and actionable assistance.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Persona and Profile

- [x] **PERS-01**: User can define assistant persona settings (name, tone, response style) and the assistant uses them consistently across sessions
- [x] **PERS-02**: User can define and update personal profile preferences that influence assistant behavior

### Session and Context

- [x] **SESS-01**: User can create, continue, and switch conversation sessions with persistent history
- [ ] **SESS-02**: User can resume a previous session and retain relevant conversational context
- [x] **SESS-03**: User can view session metadata (title, last activity) to manage ongoing conversations

### Persistent Memory

- [ ] **MEM-01**: User can save durable memory items that are reused in future conversations
- [ ] **MEM-02**: User can view, edit, and delete memory items through explicit controls
- [ ] **MEM-03**: User can disable memory for a temporary session mode without deleting stored memories

### Personal Knowledge Vault

- [ ] **KNOW-01**: User can ingest personal documents or notes into a private knowledge vault
- [ ] **KNOW-02**: User can query the knowledge vault and receive grounded answers with citations
- [ ] **KNOW-03**: User can search stored knowledge entries by keyword or semantic relevance

### Tool Execution System

- [ ] **TOOL-01**: User can register and run approved personal tools from assistant requests
- [ ] **TOOL-02**: User must confirm tool executions that can create side effects before execution
- [ ] **TOOL-03**: User can view tool execution results and failure status in conversation context

### Privacy and Continuity

- [x] **PRIV-01**: User data remains private and scoped to a single-user personal workspace
- [ ] **PRIV-02**: User can remove personal data artifacts (memory/knowledge/session) from the system
- [ ] **SYNC-01**: User can access consistent assistant state across Windows and Android clients

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Assistant Enhancements

- **ENH-01**: User receives proactive briefings/reminders tied to explicit goals
- **ENH-02**: User can switch context profiles (Home/Work/Travel) for scoped behavior
- **ENH-03**: User can inspect explainable memory traces for why memories/documents were used

### Advanced Capability

- **AUTO-01**: User can build advanced multi-step workflows with branching and retries
- **PRIV-03**: User can run broader local-first sensitive-data processing paths
- **CONN-01**: User can connect optional external apps while preserving personal-first safety controls

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Telegram, Discord, WhatsApp, or other social bot integrations | Explicitly outside personal assistant vision |
| Multi-user SaaS tenancy and enterprise RBAC | Adds complexity that conflicts with personal-first scope |
| Enterprise admin dashboards and organization controls | Not needed for single-user product goals |
| Broad multi-channel messaging platform behavior | Product is assistant-centric, not channel-centric |
| Building all OpenClaw-inspired systems in one phase | Violates staged, testable, minimal-phase delivery principle |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PERS-01 | Phase 1 | Complete |
| PERS-02 | Phase 1 | Complete |
| SESS-01 | Phase 1 | Complete |
| SESS-02 | Phase 1 | Pending |
| SESS-03 | Phase 1 | Complete |
| MEM-01 | Phase 2 | Pending |
| MEM-02 | Phase 2 | Pending |
| MEM-03 | Phase 2 | Pending |
| KNOW-01 | Phase 3 | Pending |
| KNOW-02 | Phase 3 | Pending |
| KNOW-03 | Phase 3 | Pending |
| TOOL-01 | Phase 4 | Pending |
| TOOL-02 | Phase 4 | Pending |
| TOOL-03 | Phase 4 | Pending |
| PRIV-01 | Phase 1 | Complete |
| PRIV-02 | Phase 5 | Pending |
| SYNC-01 | Phase 5 | Pending |

**Coverage:**
- v1 requirements: 17 total
- Mapped to phases: 17
- Unmapped: 0

---
*Requirements defined: 2026-03-17*
*Last updated: 2026-03-17 after roadmap mapping*
