# Build Troubleshooting (.NET MAUI)

Dokumen ini fokus untuk error build MAUI seperti:

- `Access to the path ...\obj\Debug\net8.0-android\android\assets is denied`
- `Unable to remove directory ...\resizetizer\r`
- `Microsoft.Maui.Resizetizer` / `MSB3231` access denied

## Kenapa ini sering terjadi

Ini biasanya bukan bug logika aplikasi, tetapi masalah file lock pada folder intermediate build (`obj`, `bin`, `resizetizer`).

Penyebab umum:

- Visual Studio / MSBuild / Hot Reload masih memegang file
- Android Emulator/device debugging session masih aktif
- OneDrive sedang sync folder `obj/bin`
- Antivirus/Defender memindai file intermediate
- File/folder intermediate beratribut read-only

## Langkah perbaikan (manual, urutan disarankan)

1. Stop debugging di Visual Studio.
2. Tutup Android Emulator (jika dipakai).
3. Tutup Visual Studio.
4. Dari root repository, jalankan:

   ```powershell
   scripts\clean-build-artifacts.ps1
   ```

5. Jika masih gagal, jalankan juga pembersihan `.vs`:

   ```powershell
   scripts\clean-build-artifacts.ps1 -IncludeVs
   ```

6. Buka Visual Studio lagi.
7. Restore NuGet packages.
8. Build ulang project.
9. Jika masih gagal, pindahkan repository ke folder **non-OneDrive**:

   - `C:\dev\chat-ayi`
   - `D:\dev\chat-ayi`

10. Pastikan resource tidak read-only:

   ```powershell
   attrib -R ChatAyi\Resources\* /S /D
   ```

## Solusi wajib jika error tetap muncul di Visual Studio

1. Stop debugging.
2. Tutup Visual Studio.
3. Tutup Android Emulator.
4. Jalankan sebagai user normal:

   ```powershell
   scripts\clean-build-artifacts.ps1 -IncludeVs
   ```

5. Jika masih gagal, restart Windows.
6. Pindahkan repo dari:

   `C:\Users\ASUS\OneDrive\Documents\github\chat-ayi`

   ke:

   `C:\dev\chat-ayi`

7. Build ulang.

## Catatan penting OneDrive

Lokasi repo di dalam OneDrive sering menyebabkan lock pada `obj/bin` saat proses sinkronisasi berjalan bersamaan dengan build MAUI (Android/Windows + Resizetizer). Lokasi paling aman untuk build adalah folder lokal non-OneDrive.
