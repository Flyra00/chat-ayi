# Roadmap: ChatAyi (Personal AI Assistant)

## Overview

Roadmap ini memecah transformasi ChatAyi menjadi 5 phase kecil, teruji, dan berurutan. Setiap phase hanya menambah satu kapabilitas inti agar implementasi tetap sederhana, stabil, dan mudah dieksekusi secara atomik.

## Phase 1: Identitas Assistant dan Fondasi Session

**Tujuan:**
- Membentuk persona assistant yang konsisten (nama, gaya bahasa, tone)
- Membentuk user profile dasar untuk preferensi personal
- Memperbaiki penyusunan context chat per session
- Memperbaiki penggunaan session/history agar percakapan bisa dilanjutkan dengan benar

**Hasil konkret (testable):**
- Persona dan profile tersimpan serta memengaruhi respons assistant
- User bisa membuat, memilih, dan melanjutkan session
- Riwayat session tetap konsisten saat aplikasi dibuka ulang

**Di luar scope (explicit):**
- Persistent memory lintas topik
- Tool execution
- Knowledge vault dan retrieval
- Fitur multi-user atau integrasi bot sosial

**Requirements:** [PERS-01, PERS-02, SESS-01, SESS-02, SESS-03, PRIV-01]

**Plans:** 3 plans

Plans:
- [x] 01-01-PLAN.md — Bentuk kontrak typed persona/profile/session dan store lokal single-user
- [x] 01-02-PLAN.md — Implementasi context assembler terpusat dan wiring lintas jalur chat
- [x] 01-03-PLAN.md — Implementasi UX create/switch/resume session dengan metadata terpersisten

## Phase 2: Fondasi Persistent Memory

**Tujuan:**
- Menambahkan model memory personal yang terstruktur
- Menambahkan penyimpanan dan pengambilan memory
- Menyuntikkan memory relevan ke prompt context

**Hasil konkret (testable):**
- User dapat menyimpan, melihat, mengubah, dan menghapus memory
- Memory relevan muncul pada respons percakapan berikutnya
- Tersedia mode sesi tanpa simpan memory baru (temporary mode)

**Di luar scope (explicit):**
- Eksekusi tools
- Ingest dan retrieval dokumen knowledge
- Optimasi UX lanjutan (summary/suggestion tingkat lanjut)

## Phase 3: Eksekusi Tool yang Aman

**Tujuan:**
- Memperkenalkan interface tool yang sederhana dan extensible
- Menambahkan beberapa tool personal yang aman
- Mengintegrasikan penggunaan tool ke alur assistant

**Hasil konkret (testable):**
- Assistant dapat memanggil tool terdaftar melalui kontrak interface tunggal
- Aksi yang berdampak wajib konfirmasi user sebelum dieksekusi
- Hasil atau error tool tampil jelas di konteks percakapan

**Di luar scope (explicit):**
- Marketplace plugin
- Tool yang terhubung ke platform sosial/media
- Otomasi otonom tanpa persetujuan user

## Phase 4: Personal Knowledge Vault

**Tujuan:**
- Mendukung penyimpanan note/dokumen pribadi
- Menambahkan retrieval ringan untuk knowledge personal
- Memisahkan knowledge vault dari memory personal

**Hasil konkret (testable):**
- User dapat ingest note/dokumen ke vault pribadi
- Assistant dapat menjawab pertanyaan berbasis vault dengan referensi sumber
- Data knowledge tersimpan terpisah dari memory profile/percakapan

**Di luar scope (explicit):**
- Knowledge graph kompleks
- Sinkronisasi connector eksternal skala besar
- Optimasi retrieval tingkat lanjut (reranking kompleks multi-stage)

## Phase 5: Penyempurnaan Pengalaman Personal Assistant

**Tujuan:**
- Meningkatkan kualitas summary percakapan
- Meningkatkan suggestion yang relevan terhadap konteks personal
- Meningkatkan UX assistant secara keseluruhan (kejelasan alur dan feedback)

**Hasil konkret (testable):**
- Summary sesi lebih ringkas, konsisten, dan membantu melanjutkan konteks
- Suggestion yang muncul lebih relevan dengan profile, memory, dan knowledge
- Interaksi assistant terasa lebih cepat dipahami dan lebih actionable

**Di luar scope (explicit):**
- Refactor arsitektur besar-besaran
- Penambahan sistem enterprise/admin
- Ekspansi ke multi-channel bot platform

## Execution Rules

- Kerjakan hanya satu phase aktif pada satu waktu
- Tiap phase selesai dulu (implementasi + uji) sebelum lanjut phase berikutnya
- Jika ada dependensi phase berikutnya, cukup siapkan placeholder/interface
- Hindari penambahan fitur di luar scope phase aktif

## Progress

| Phase | Status | Catatan |
|-------|--------|---------|
| 1. Identitas Assistant dan Fondasi Session | Complete | 3/3 plan selesai (01-01, 01-02, 01-03 complete) |
| 2. Fondasi Persistent Memory | Not started | Memory lifecycle dasar |
| 3. Eksekusi Tool yang Aman | Not started | Tool aman dengan konfirmasi |
| 4. Personal Knowledge Vault | Not started | Retrieval ringan dan pemisahan data |
| 5. Penyempurnaan Pengalaman Personal Assistant | Not started | Summary, suggestion, UX |
