# Phase 2: Fondasi Persistent Memory - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase ini membangun fondasi persistent memory personal untuk ChatAyi: memory entity sederhana, penyimpanan persisten, retrieval relevansi sederhana, injeksi memory ke ContextAssembler, dan aturan kapan memory disimpan. Memory harus terpisah dari user profile, session history, dan data search/browse.

Di luar scope phase ini: knowledge vault/RAG berat/vector search, tool system, multi-agent, dan perubahan arsitektur besar.

</domain>

<decisions>
## Implementation Decisions

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

</decisions>

<specifics>
## Specific Ideas

- Phase 2 harus tetap ringan: model C# sederhana + penyimpanan lokal.
- Retrieval dimulai dari pendekatan sederhana (tanpa embedding/vector DB).
- Injeksi memory harus membantu percakapan berikutnya tanpa membuat persona/session context kalah prioritas.

</specifics>

<deferred>
## Deferred Ideas

- Implicit memory extraction otomatis dari pola percakapan (di luar Phase 2).
- Retrieval berbasis embedding/vector search dan RAG yang lebih berat (fase lanjutan).
- Perluasan kategori memory penuh (tujuan jangka pendek, kebiasaan detail) setelah fondasi stabil.

</deferred>

---

*Phase: 02-fondasi-persistent-memory*
*Context gathered: 2026-03-17*
