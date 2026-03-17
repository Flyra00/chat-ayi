# Phase 1: Identitas Assistant dan Fondasi Session - Context

**Gathered:** 2026-03-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase ini hanya membangun fondasi identitas assistant dan kualitas perakitan konteks sesi: model persona assistant, model user profile dasar, peningkatan context builder, dan penggunaan session/history sebelum pemanggilan model.

Di luar scope phase ini: persistent memory retrieval, embedding, vector search, tool execution system, document knowledge vault, voice, automation workflow, dan multi-agent behavior.

</domain>

<decisions>
## Implementation Decisions

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

</decisions>

<specifics>
## Specific Ideas

- Target utama phase ini adalah membuat ChatAyi terasa personal AI assistant, bukan aplikasi chat generik.
- Preferensi implementasi: perubahan aditif, minim rewrite, modular, mudah diuji, dan mempertahankan arsitektur yang sudah ada.
- Area UX session/history yang lebih luas ditunda; hanya keputusan minimum yang dibutuhkan untuk konteks builder dimasukkan di phase ini.

</specifics>

<deferred>
## Deferred Ideas

- Perilaku UX session/history yang lebih jauh (mis. aturan rename/title UX detail, alur pengelolaan session lanjutan) ditunda ke fase berikutnya jika masih dibutuhkan.
- Perluasan persona/profile yang lebih kaya dari kontrak minimal ditunda setelah fondasi Phase 1 stabil.

</deferred>

---

*Phase: 01-identitas-assistant-dan-fondasi-session*
*Context gathered: 2026-03-17*
