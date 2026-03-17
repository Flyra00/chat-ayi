# Phase 1: Identitas Assistant dan Fondasi Session - Research

**Researched:** 2026-03-17
**Domain:** Persona/profile contract, prompt context assembly, dan session continuity di .NET MAUI + ASP.NET Core
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
### Kontrak Persona Assistant
- Persona menggunakan kontrak kecil agar stabil dan testable di Phase 1.
- `role_statement` bersifat fixed global (satu versi aktif untuk seluruh aplikasi).
- Prioritas instruksi dikunci: `System (safety + app boundaries) > Persona (role_statement, tone, response_style) > User Profile > Session Context > User Message`.
- `tone` enum dikunci: `calm | toxic | professional`.
- Karakter persona (`blunt / sarkastik / realistis`) disimpan sebagai directive persona, bukan sebagai enum tone.
- Gaya persona boleh lebih tegas/sarkastik sesuai preferensi user, tetapi tetap tidak boleh melanggar safety rules dan app boundaries.

### Batas User Profile Dasar
- User profile Phase 1 dibatasi ke 5 field minimal:
  - `display_name`
  - `preferred_language`
  - `timezone`
  - `response_length_preference` (`brief|balanced|detailed`)
  - `formality_preference` (`casual|neutral|formal`)
- Source of truth profile: satu profil global untuk single-user, berlaku lintas session.
- Jika profil belum lengkap, gunakan fallback default:
  - `display_name=User`
  - `preferred_language=id-ID`
  - `timezone=Asia/Jakarta`
  - `response_length_preference=balanced`
  - `formality_preference=neutral`
- Pengaruh user profile dibatasi ke format keluaran (bahasa, panjang jawaban, formalitas), bukan mengubah inti reasoning.

### Kebijakan Perakitan Prompt Context
- Harus ada satu titik perakitan konteks yang jelas sebelum model call (context assembler terpusat).
- Urutan blok perakitan konteks dikunci:
  1. System safety + app boundaries
  2. Persona block
  3. User profile block
  4. Session context block
  5. User message terbaru
- Session context memakai kombinasi:
  - recent turns: 6 turn terakhir
  - summary ringan: maksimal 5 bullet
- Saat konflik konteks, recent turns menjadi sumber kebenaran utama dibanding summary.
- Jika summary belum ada/gagal, fallback ke recent turns saja (jangan memblokir alur chat).

### OpenCode's Discretion
- Struktur class dan penamaan properti C# selama mengikuti keputusan kontrak di atas.
- Detail implementasi validasi enum/field (misalnya validator layer) selama tidak menambah dependency besar.
- Strategi ringkas pembuatan summary session ringan selama tetap mengikuti batas 5 bullet.

### Deferred Ideas (OUT OF SCOPE)
- Perilaku UX session/history yang lebih jauh (mis. aturan rename/title UX detail, alur pengelolaan session lanjutan) ditunda ke fase berikutnya jika masih dibutuhkan.
- Perluasan persona/profile yang lebih kaya dari kontrak minimal ditunda setelah fondasi Phase 1 stabil.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| PERS-01 | User can define assistant persona settings (name, tone, response style) and the assistant uses them consistently across sessions | Kontrak `AssistantPersona` typed + central `PromptContextAssembler` dengan urutan prioritas tetap, disuntikkan di semua jalur request chat. |
| PERS-02 | User can define and update personal profile preferences that influence assistant behavior | `UserProfile` global single-user + default fallback + mapping eksplisit ke format instruction (language/length/formality). |
| SESS-01 | User can create, continue, and switch conversation sessions with persistent history | Session catalog + transcript terpisah per session (`.jsonl`) + active session pointer terpersisten. |
| SESS-02 | User can resume a previous session and retain relevant conversational context | `SessionContextSnapshot` (recent 6 + summary <=5 bullet) dibaca dari store sebelum model call. |
| SESS-03 | User can view session metadata (title, last activity) to manage ongoing conversations | Simpan `SessionMeta` (`session_id`, `title`, `last_activity_utc`) yang di-update saat append turn. |
| PRIV-01 | User data remains private and scoped to a single-user personal workspace | Tetap local-first (`FileSystem.AppDataDirectory`/workspace) dan jangan kirim profile/session metadata ke provider selain context yang memang dibutuhkan model. |
</phase_requirements>

## Summary

Phase 1 paling aman dikerjakan sebagai refactor arsitektural kecil: ekstrak logika perakitan prompt dari `ChatPage` ke satu service terpusat, tambah kontrak persona/profile yang typed, dan rapikan fondasi session agar bukan lagi hanya satu `SessionId` implisit. Saat ini, prompt assembly tersebar di beberapa cabang command/chat di `ChatPage.xaml.cs`, persona belum punya model eksplisit, dan profile user masih implicit di hardcoded instruction (mis. "Reply in Indonesian").

Fondasi session sebenarnya sudah ada (append/read `.jsonl` di `LocalSessionStore` dan `SessionStore`), tetapi masih level "append transcript" tanpa catalog metadata, tanpa session switching, dan tanpa summary ringkas yang dipakai konsisten untuk resume. Ini berarti kebutuhan SESS-01/02/03 belum terpenuhi penuh meskipun primitive penyimpanan sudah tersedia.

Karena batas fase meminta perubahan aditif dan minim rewrite, rekomendasi terbaik adalah mempertahankan storage file-based yang sudah ada, menambahkan layer model + assembler + metadata index, lalu mengaitkannya ke UI/flow yang sudah berjalan. Ini memberi jalur implementasi cepat, testable, dan tetap sejalan dengan scope personal single-user.

**Primary recommendation:** Implement `PromptContextAssembler` + typed `AssistantPersona/UserProfile/SessionMeta` first, then wire session catalog and resume snapshot on top of existing JSONL stores.

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET SDK / Runtime | 8.0 (`net8.0`, `net8.0-android`, `net8.0-windows`) | Runtime utama app + API | Sudah jadi baseline project dan stabil untuk MAUI + ASP.NET Core. |
| ASP.NET Core Minimal API | .NET 8 line | Endpoint backend ringan (`Program.cs` mapping langsung) | Cocok untuk service kecil, dependency injection sederhana, dan iterasi cepat. |
| .NET MAUI Storage (`Preferences`, `SecureStorage`) | MAUI sesuai workload .NET 8 | Persistensi setting/profile dan secret key | Official API platform-aware untuk Android/Windows; sudah dipakai di codebase. |
| `System.Text.Json` | Built-in .NET 8 | Serialisasi record/profile/session (`.jsonl`) | Cepat, native, tanpa dependency eksternal tambahan. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.Text.RegularExpressions` | Built-in | Validasi `sessionId` aman | Saat menerima/membuat session ID agar path-safe. |
| `SemaphoreSlim` | Built-in | Guard untuk queue/load operasi async | Saat update queue/session summary agar race condition terkendali. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| JSONL file session store | SQLite | SQLite lebih kuat untuk query metadata, tapi overkill untuk scope fase dan menambah migration/testing surface. |
| Custom crypto untuk key | Manual AES + key mgmt | Risiko security bug lebih tinggi; `SecureStorage` sudah platform-native. |
| Scattered prompt construction | Inline per feature branch | Mempercepat awal, tapi cepat drift dan sulit menjamin urutan prioritas instruksi. |

**Installation:**
```bash
dotnet restore ChatAyi.sln
```

## Architecture Patterns

### Recommended Project Structure
```
ChatAyi/
├── Models/
│   ├── AssistantPersona.cs      # role_statement, tone, response_style directives
│   ├── UserProfile.cs           # 5 field profile + defaults
│   └── SessionMeta.cs           # session id/title/last activity
├── Services/
│   ├── PromptContextAssembler.cs  # single prompt assembly entrypoint
│   ├── LocalSessionStore.cs       # transcript read/write + recent turns
│   └── SessionCatalogStore.cs     # session metadata index + active session
└── Pages/
    └── ChatPage.xaml.cs         # orchestration only, not prompt rules
```

### Pattern 1: Central Prompt Assembler (Mandatory)
**What:** Seluruh request model melewati satu fungsi yang menyusun blok context dalam urutan terkunci keputusan phase.
**When to use:** Semua jalur chat (`normal`, `/search`, `/browse`, dan resume flow).
**Example:**
```csharp
var prompt = assembler.Build(new PromptBuildInput
{
    SafetyBlock = safety,
    Persona = persona,
    UserProfile = profile,
    SessionContext = snapshot,
    UserMessage = promptText
});
```

### Pattern 2: Session Snapshot = Recent Truth + Optional Summary
**What:** Bentuk `SessionContextSnapshot` dari `recentTurns(6)` + `summaryBullets(<=5)`; saat konflik, recent turns menang.
**When to use:** Tepat sebelum model call dan saat resume session lama.
**Example:**
```csharp
var recent = await sessions.ReadRecentChatAsync(sessionId, 6, ct);
var summary = await summaries.TryReadSummaryAsync(sessionId, ct); // nullable
var snapshot = SessionContextSnapshot.Create(recent, summary);
```

### Pattern 3: Session Catalog + Transcript Split
**What:** Pisahkan `session-meta.json` (list metadata) dari `sessions/{id}.jsonl` (turn detail).
**When to use:** Saat create/switch/list session untuk memenuhi SESS-01/03 tanpa scan semua file transcript tiap render.
**Example:**
```csharp
await catalog.UpsertAsync(new SessionMeta(id, title, DateTimeOffset.UtcNow), ct);
await transcripts.AppendAsync(id, turn, ct);
```

### Anti-Patterns to Avoid
- **Prompt assembly tersebar di UI branch:** bikin drift instruksi dan sulit audit prioritas context.
- **Summary overwrite recent turns:** melanggar keputusan bahwa recent turns adalah sumber utama saat konflik.
- **Mengambil semua history mentah ke prompt:** boros context window, menaikkan latency, dan menurunkan konsistensi.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Secret storage | Enkripsi key custom | `SecureStorage` | API official, handling platform-specific keystore/DPAPI sudah disediakan. |
| App settings/profile prefs | Parser file config custom untuk key-value kecil | `Preferences` | Sudah native, ringan, cukup untuk profile kecil non-secret. |
| JSON parser manual | String-split JSON line | `System.Text.Json` | Mengurangi bug parsing dan menjaga schema compatibility. |
| Session ID sanitization | Regex ad-hoc per callsite | Satu validator utility (`SafeId`) | Hindari ketidakkonsistenan validasi/path traversal risk. |

**Key insight:** Untuk fase fondasi, kecepatan dan stabilitas datang dari memusatkan aturan domain (persona/profile/context/session), bukan dari menambah storage/infra baru.

## Common Pitfalls

### Pitfall 1: Persona/Profile tidak konsisten antar jalur command
**What goes wrong:** Jalur `/search` dan `/browse` pakai prompt yang beda style dibanding chat biasa.
**Why it happens:** Prompt string dirakit inline di banyak branch `ChatPage`.
**How to avoid:** Semua jalur panggil assembler yang sama; input berbeda, rule sama.
**Warning signs:** Respons berubah tone/formality saat command berubah walau user preference sama.

### Pitfall 2: Session switch hanya mengganti ID tanpa reload context
**What goes wrong:** UI terlihat pindah session tapi context model masih dari history lama di memory collection.
**Why it happens:** `Messages` state lokal tidak disinkronkan dengan transcript session target.
**How to avoid:** Saat switch, hydrate message list dari transcript session target + reset ephemeral state.
**Warning signs:** Pertanyaan lanjutan menjawab topik session sebelumnya.

### Pitfall 3: Metadata session drift
**What goes wrong:** `title`/`last_activity` tidak akurat atau tertinggal.
**Why it happens:** Append transcript tidak meng-update catalog metadata secara atomik.
**How to avoid:** Bungkus append turn + update metadata dalam satu service method.
**Warning signs:** Urutan daftar session tidak sesuai aktivitas terbaru.

### Pitfall 4: Privacy scope melebar tidak sengaja
**What goes wrong:** Data profile/session terkirim lebih dari yang dibutuhkan ke provider upstream.
**Why it happens:** Mapping prompt tidak eksplisit antara field profile dan instruction.
**How to avoid:** Gunakan mapper terkontrol yang hanya ekspor bahasa/panjang/formalitas + persona contract.
**Warning signs:** Payload request berisi field internal yang tidak relevan untuk jawaban.

## Code Examples

Verified patterns from current codebase and official docs:

### Append & Read Session Transcript (existing baseline)
```csharp
await _sessions.AppendAsync(sessionId, new
{
    ts = DateTimeOffset.UtcNow,
    role = "user",
    content = prompt,
    model
}, ct);

var recent = await _sessions.ReadRecentChatAsync(sessionId, 6, ct);
```
Source: `ChatAyi/Pages/ChatPage.xaml.cs`, `ChatAyi/Services/LocalSessionStore.cs`

### Minimal API route + DI baseline
```csharp
builder.Services.AddSingleton<SessionStore>();

app.MapPost("/api/chat/completions", async (
    HttpContext ctx,
    SessionStore sessions) =>
{
    var sessionId = sessions.GetSessionId(ctx);
    // ...
});
```
Source: `ChatAyi.Api/Program.cs`, Microsoft Learn Minimal APIs

### Official preference/secret storage usage
```csharp
Preferences.Default.Set("profile.formality", "neutral");
var formality = Preferences.Default.Get("profile.formality", "neutral");

await SecureStorage.Default.SetAsync("CEREBRAS_API_KEY", apiKey);
```
Source: Microsoft Learn MAUI Preferences/SecureStorage

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Prompt dirakit inline per command path | Centralized prompt assembler service | Target Phase 1 | Konsistensi persona/profile meningkat, lebih testable. |
| Single implicit session (`GetOrCreateSessionId`) | Session catalog + explicit create/switch/active pointer | Target Phase 1 | Memenuhi SESS-01/03 tanpa rewrite storage besar. |
| History-only context slicing dari UI list | Session snapshot (recent 6 + optional summary <=5) | Target Phase 1 | Resume context lebih stabil dan hemat token. |
| Hardcoded language/style hints | User profile mapped to style instruction | Target Phase 1 | PERS-02 terpenuhi dengan kontrol yang jelas. |

**Deprecated/outdated:**
- Inline hardcoded prompt style di banyak callsite: diganti assembler terpusat.
- Reliance pada satu session ID global tanpa catalog metadata: tidak cukup untuk SESS-01/03.

## Open Questions

1. **Lokasi source-of-truth persona/profile (client-only vs backend mirror)**
   - What we know: App saat ini local-first, single-user, dan chat call utama dari client.
   - What's unclear: Apakah backend juga perlu membaca persona/profile secara langsung di fase ini.
   - Recommendation: Tetapkan client-local sebagai source-of-truth Phase 1; backend tetap stateless terhadap profile kecuali dibutuhkan endpoint baru.

2. **Strategi penentuan `session title` awal**
   - What we know: Requirement butuh metadata title + last activity.
   - What's unclear: Rule title deterministic (first user message, truncation, manual rename).
   - Recommendation: Phase 1 gunakan rule sederhana deterministic: judul dari user message pertama (trim + max length), rename UX detail ditunda (sesuai deferred).

## Sources

### Primary (HIGH confidence)
- `ChatAyi/Pages/ChatPage.xaml.cs` - current prompt assembly paths, session append flow, context handling
- `ChatAyi/Services/LocalSessionStore.cs` - JSONL transcript persistence and recent-turn retrieval
- `ChatAyi.Api/Sessions/SessionStore.cs` - server-side session header + transcript pattern
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis - Minimal API route handlers, DI, response patterns
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences - profile/preferences storage constraints
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage - secure key storage behavior and limits

### Secondary (MEDIUM confidence)
- `README.md`, `.planning/ROADMAP.md`, `.planning/PROJECT.md` - intended architecture direction and phase scope

### Tertiary (LOW confidence)
- None

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - langsung terverifikasi dari `.csproj`, codebase, dan official docs.
- Architecture: HIGH - rekomendasi berbasis gap nyata antara requirement phase dan implementasi saat ini.
- Pitfalls: MEDIUM - berasal dari pola code saat ini; beberapa butuh validasi saat implementasi nyata UI switch session.

**Research date:** 2026-03-17
**Valid until:** 2026-04-16
