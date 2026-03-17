# Phase 2: Fondasi Persistent Memory - Research

**Researched:** 2026-03-17
**Domain:** Structured personal memory for .NET MAUI chat context assembly
**Confidence:** MEDIUM

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

### Kriteria Simpan Memory
- Memory disimpan dengan mode **explicit-only** (tidak implicit auto-capture di Phase 2).
- Trigger simpan menerima dua bentuk: command formal dan intent natural user (contoh: "ingat ini", "tolong simpan ini").
- Cakupan memory awal dibatasi ke 3 kategori prioritas:
  - preferensi user
  - project aktif user
  - info penting yang user minta untuk diingat
- Jika user minta simpan informasi sensitif, sistem tetap mengikuti permintaan user dalam batas safety/app boundaries global.

### Relevansi Retrieval (Sederhana)
- Maksimal memory yang diinjeksi per request: **5 item**.
- Seleksi relevansi memakai **keyword overlap sederhana**.
- Threshold relevansi: minimal **2 keyword cocok**.
- Jika tidak ada memory relevan, **jangan injeksi memory block**.

### Injeksi Memory ke ContextAssembler
- Urutan context dikunci menjadi:
  `System > Persona > User Profile > Memory > Session Context > User Message`
- Format memory block: bullet list ringkas + tag kategori per item.
- Instruksi memory block: memory hanya referensi, dipakai jika relevan, bukan fakta mutlak.
- Jika memory bertentangan dengan pesan user terbaru, pesan user terbaru menang; memory tetap sebagai referensi.

### OpenCode's Discretion
- Detail normalisasi token/keyword untuk retrieval sederhana.
- Penamaan kategori memory internal sepanjang tetap memetakan ke 3 kategori yang dikunci.
- Frasa template prompt memory block selama tetap menjaga prioritas context yang sudah diputuskan.

### Deferred Ideas (OUT OF SCOPE)
- Implicit memory extraction otomatis dari pola percakapan (di luar Phase 2).
- Retrieval berbasis embedding/vector search dan RAG yang lebih berat (fase lanjutan).
- Perluasan kategori memory penuh (tujuan jangka pendek, kebiasaan detail) setelah fondasi stabil.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| MEM-01 | User can save durable memory items that are reused in future conversations | Memory entity terstruktur + file-backed `MemoryStore` + retrieval relevansi (top 5, threshold >=2) + injeksi ke `PromptContextAssembler`. |
| MEM-02 | User can view, edit, and delete memory items through explicit controls | Tambahkan API service internal untuk list/update/delete by `memory_id`; surface explicit controls (command/UI panel) tanpa implicit capture. |
| MEM-03 | User can disable memory for a temporary session mode without deleting stored memories | Tambahkan session-scoped memory mode flag (off for current session) yang hanya mem-bypass retrieval/injection, bukan storage delete. |
</phase_requirements>

## Summary

Fondasi phase ini paling aman dibangun di jalur arsitektur yang sudah dipakai Phase 1: model ternormalisasi, storage lokal JSON di `FileSystem.AppDataDirectory`, dan akses serialized dengan `SemaphoreSlim`. Codebase saat ini sudah punya memory retrieval ringan (`LocalMemoryStore`) tapi masih berbasis chunk file markdown (`MEMORY.md` + daily notes) dan belum punya model personal memory terstruktur, CRUD eksplisit, atau mode disable memory per-session.

Untuk memenuhi MEM-01/02/03 tanpa rewrite besar, gunakan satu dokumen memory terstruktur (misalnya `personal-memory.json`) berisi list item durable dengan kategori terkontrol, metadata dasar, dan operasi CRUD eksplisit. Retrieval tetap sederhana sesuai keputusan user: normalisasi keyword, hitung overlap, ambil maksimal 5 item dengan minimal 2 keyword cocok. Injeksi masuk ke `PromptContextAssembler` pada slot baru antara profile dan session context, dengan aturan konflik "user terbaru menang".

Karena target stack adalah .NET MAUI net8 dan project sudah mengandalkan `System.Text.Json`, `Preferences`, `FileSystem.AppDataDirectory`, pendekatan ini menjaga konsistensi teknis dan menghindari dependency baru (SQLite/vector DB) yang tidak dibutuhkan di phase ini.

**Primary recommendation:** Implement `PersonalMemoryStore` berbasis JSON file + explicit CRUD + session-scoped memory disable flag, lalu ubah `PromptContextAssembler` agar menyuntikkan memory block terurut dan bersyarat (hanya jika relevan).

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET MAUI | net8.0 (repo target) | Cross-platform app runtime (Android/Windows) | Sudah jadi baseline project dan menyatukan storage APIs lintas platform. |
| `Microsoft.Maui.Storage.FileSystem` | MAUI built-in | Lokasi persistence lokal (`AppDataDirectory`) | Direkomendasikan resmi untuk app data directory lintas platform. |
| `System.Text.Json` | .NET 8 built-in | Serialize/deserialize memory document | Sudah dipakai di store existing; cepat dan tanpa dependency tambahan. |
| `System.Threading.SemaphoreSlim` | .NET 8 built-in | Serialize akses read/write dokumen memory | Cocok untuk sinkronisasi intra-app async dan sudah pola existing. |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `Microsoft.Maui.Storage.Preferences` | MAUI built-in | Simpan flag ringan (mis. memory mode toggle) | Untuk key/value kecil, bukan payload memory besar. |
| `System.Text.RegularExpressions` | .NET 8 built-in | Normalisasi token/keyword retrieval | Saat butuh tokenization sederhana tanpa search engine. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| JSON file store + simple overlap | SQLite FTS | Lebih kuat untuk scale, tapi overkill untuk phase ini dan menambah kompleksitas migration/query. |
| Keyword overlap threshold | Embedding/vector retrieval | Relevansi lebih canggih, tapi sudah ditetapkan out-of-scope pada deferred ideas. |
| Session-scoped bypass flag | Hard delete memories saat mode off | Melanggar MEM-03 karena mode harus temporary tanpa menghapus data. |

**Installation:**
```bash
# No new package required for Phase 2 baseline
```

## Architecture Patterns

### Recommended Project Structure
```
ChatAyi/
├── Models/
│   ├── PersonalMemoryItem.cs      # Entity memory durable + category + metadata
│   └── PersonalMemoryDocument.cs  # Root document + normalization helpers
├── Services/
│   ├── PersonalMemoryStore.cs     # CRUD + retrieval + persistence locking
│   └── PromptContextAssembler.cs  # Inject Memory block between Profile and Session
└── Pages/
    └── ChatPage.xaml(.cs)         # Explicit controls + session temporary memory mode
```

### Pattern 1: File-Backed Document Store with Async Lock
**What:** Simpan semua memory items dalam satu JSON document, akses lewat mutex (`SemaphoreSlim`) untuk hindari race write.
**When to use:** Saat storage lokal single-user, data volume kecil-menengah, dan phase menuntut kesederhanaan.
**Example:**
```csharp
// Source: Existing pattern in ChatAyi/Services/SessionCatalogStore.cs
await _mutex.WaitAsync(ct);
try
{
    var doc = await ReadDocumentAsync(ct);
    doc.Items = doc.Items
        .Where(x => x.IsActive)
        .OrderByDescending(x => x.UpdatedUtc)
        .ToList();

    await File.WriteAllTextAsync(filePath, JsonSerializer.Serialize(doc, _jsonOptions), ct);
}
finally
{
    _mutex.Release();
}
```

### Pattern 2: Retrieval by Normalized Keyword Overlap
**What:** Normalisasi query + memory text, hitung keyword overlap, loloskan item jika overlap >=2, ambil top 5.
**When to use:** Sesuai keputusan phase: retrieval sederhana tanpa embedding/vector.
**Example:**
```csharp
// Source: Adapted from LocalMemoryStore scoring + Phase 2 decisions
var tokens = Normalize(query)
    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
    .Distinct(StringComparer.Ordinal)
    .ToList();

var ranked = memories
    .Select(m => new { Item = m, MatchCount = CountKeywordOverlap(m.Haystack, tokens) })
    .Where(x => x.MatchCount >= 2)
    .OrderByDescending(x => x.MatchCount)
    .Take(5)
    .Select(x => x.Item)
    .ToList();
```

### Pattern 3: Deterministic Context Layering
**What:** Bangun prompt dengan urutan tetap dan memory block kondisional.
**When to use:** Setiap request chat/search/browse yang lewat `PromptContextAssembler`.
**Example:**
```csharp
// Source: Existing PromptContextAssembler + locked context order from 02-CONTEXT.md
return new List<object>
{
    new { role = "system", content = safetyBlock },
    new { role = "system", content = personaBlock },
    new { role = "system", content = profileBlock },
    new { role = "system", content = memoryBlock }, // only when relevant items exist
    new { role = "system", content = sessionBlock },
    new { role = "user", content = userMessage }
};
```

### Anti-Patterns to Avoid
- **Storing memory payload in `Preferences`:** Dokumentasi MAUI menegaskan preferences untuk data kecil; ada limit nilai (Windows 8KB per value).
- **Implicit auto-capture from every turn:** Melanggar locked decision explicit-only di Phase 2.
- **Injecting empty memory block:** Keputusan phase mewajibkan skip injection jika tidak ada memory relevan.
- **Category as free text unrestricted:** Menyebabkan drift, sulit retrieval, dan konflik dengan 3 kategori locked.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Persistent key/value settings | Custom config parser | `Preferences` | API lintas platform sudah siap + storage native MAUI. |
| Cross-platform app data path | Hardcoded OS paths | `FileSystem.AppDataDirectory` | Menghindari bug platform path + backup behavior sudah dikelola platform. |
| JSON serialization plumbing | Manual JSON string builder/parser | `System.Text.Json` typed models | Lebih aman, maintainable, dan sudah dipakai repo. |
| Intra-app async file locking | Custom spinlock/boolean flags | `SemaphoreSlim` | Primitive resmi untuk kontrol concurrency di single app. |

**Key insight:** Di phase ini kompleksitas terbesar bukan algoritma retrieval, tapi konsistensi state (CRUD + injection + mode session). Reuse primitive platform bawaan mengurangi bug sinkronisasi dan storage corruption.

## Common Pitfalls

### Pitfall 1: Memory Store Corruption on Concurrent Writes
**What goes wrong:** Item hilang/tertindih saat save-delete-edit terjadi berdekatan.
**Why it happens:** Read-modify-write tanpa lock async tunggal.
**How to avoid:** Satu `SemaphoreSlim` per store + semua operasi dokumen melewati critical section.
**Warning signs:** Jumlah item fluktuatif tidak konsisten setelah operasi beruntun.

### Pitfall 2: Wrong Storage Medium for Durable Memory
**What goes wrong:** Memory panjang disimpan ke `Preferences`, lalu truncate/gagal platform-specific.
**Why it happens:** Menyamakan config flags dengan data dokumen.
**How to avoid:** Simpan item memory ke file JSON di `AppDataDirectory`; gunakan `Preferences` hanya untuk toggle/flags.
**Warning signs:** Value memory besar, sulit audit file, perilaku berbeda Android vs Windows.

### Pitfall 3: Retrieval Noise from Weak Normalization
**What goes wrong:** Memory tidak relevan sering terinjeksi atau memory relevan tidak terambil.
**Why it happens:** Tokenisasi tidak konsisten (case/punctuation/duplikat) dan tanpa threshold.
**How to avoid:** Lowercase + normalize whitespace/punctuation + dedupe tokens + threshold >=2 sesuai keputusan phase.
**Warning signs:** Banyak injeksi item generik untuk query sempit.

### Pitfall 4: Breaking Context Priority Contract
**What goes wrong:** Memory meng-overwrite user latest message atau persona.
**Why it happens:** Prompt block memory terlalu kuat atau urutan salah.
**How to avoid:** Pertahankan urutan locked + instruksi eksplisit "memory adalah referensi, user terbaru menang".
**Warning signs:** Respons model mengabaikan koreksi terbaru user.

## Code Examples

Verified patterns from official sources:

### MAUI App Data Directory Access
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-system-helpers
var filePath = Path.Combine(FileSystem.Current.AppDataDirectory, "personal-memory.json");
```

### Preferences for Lightweight Session Flag
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences
Preferences.Default.Set("ChatAyi.MemoryMode.TempDisabled", true);
var disabled = Preferences.Default.Get("ChatAyi.MemoryMode.TempDisabled", false);
```

### JSON Write with Async API
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.io.file.writealltextasync
var json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(filePath, json, ct);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Markdown chunk memory (`MEMORY.md` + daily files) + free-form retrieval context | Structured personal memory entity + explicit CRUD + category tags | Phase 2 target | Data jadi bisa diedit/hapus deterministik (MEM-02). |
| Memory injection berdasarkan context string panjang | Top-5 item, overlap>=2, inject only when relevant | Phase 2 locked decisions | Prompt lebih stabil, noise turun, token budget lebih terkontrol. |
| Memory behavior implicit via `/remember` extraction flow | Explicit-only save triggers (command + natural intent) | Phase 2 locked decisions | Kontrol user naik dan side-effect tak terduga berkurang. |

**Deprecated/outdated:**
- Pure long-form memory excerpt injection tanpa structured item metadata: tidak cukup untuk MEM-02 (view/edit/delete eksplisit).

## Open Questions

1. **Surface UX untuk MEM-02 explicit controls**
   - What we know: Requirement meminta view/edit/delete eksplisit; settings overlay saat ini belum punya memory list panel.
   - What's unclear: Akan pakai command set (`/memory list|edit|delete`) dulu atau panel UI dedicated.
   - Recommendation: Start command-first untuk Phase 2 (lebih kecil dan cepat), UI panel bisa follow-up phase kecil berikutnya.

2. **Scope session temporary mode untuk MEM-03**
   - What we know: Mode harus temporary dan tidak menghapus data.
   - What's unclear: Temporary = sampai session switch, app restart, atau manual toggle on kembali.
   - Recommendation: Session-scoped sampai session switch/reopen (default on), disimpan sebagai flag ringan per session id.

## Sources

### Primary (HIGH confidence)
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/file-system-helpers - `AppDataDirectory` behavior and platform notes.
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences - `Preferences` usage and limitations.
- https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim - async synchronization semantics.
- https://learn.microsoft.com/en-us/dotnet/api/system.io.file.writealltextasync - async file overwrite behavior.
- `ChatAyi/ChatAyi/Services/PromptContextAssembler.cs` - current context layering baseline.
- `ChatAyi/ChatAyi/Services/LocalMemoryStore.cs` - existing simple retrieval/token normalization implementation.
- `ChatAyi/ChatAyi/Pages/ChatPage.xaml.cs` - current `/remember` flow and memory injection touchpoints.

### Secondary (MEDIUM confidence)
- https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/how-to - practical serialization patterns and defaults.

### Tertiary (LOW confidence)
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH - langsung cocok dengan stack existing + didukung docs resmi MAUI/.NET.
- Architecture: MEDIUM - kuat di pattern existing repo, tapi detail UX MEM-02/MEM-03 masih perlu keputusan implementasi kecil.
- Pitfalls: MEDIUM - sebagian dari docs resmi, sebagian dari analisis pattern code saat ini.

**Research date:** 2026-03-17
**Valid until:** 2026-04-16
