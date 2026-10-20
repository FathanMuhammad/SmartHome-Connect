# Smart AC Controller - Tuya Cloud REST API (WPF .NET 8)

Aplikasi desktop Windows modern dengan C# .NET 8 (WPF) untuk mengontrol Air Conditioner (AC) melalui Tuya Cloud REST API v2.0 menggunakan Tuya Smart IR Blaster.

Aplikasi ini dirancang ultra-ringan tanpa ketergantungan library pihak ketiga yang berat (hanya menggunakan bawaan .NET: `HttpClient`, `System.Security.Cryptography`, dan `System.Text.Json`).

---

## Fitur Utama

1. **Autentikasi Tuya Cloud v2.0 Mandiri (Zero Heavy Dependencies)**:
   - Perhitungan `StringToSign` dan hashing `HMAC-SHA256` sesuai spesifikasi Tuya v2.0.
   - Manajemen token otomatis (`GET /v1.0/token?grant_type=1`) dengan caching waktu kedaluwarsa.
   - Penambahan header wajib: `client_id`, `sign`, `t` (Unix milliseconds), `sign_method`, dan `access_token`.

2. **Kontrol IR Blaster AC Lengkap**:
   - Endpoint: `POST /v2.0/infrareds/{infrared_id}/air-conditioners/{remote_id}/scenes/command`
   - Payload: `{"power": 0/1, "temp": 16-30, "mode": 0-4, "wind": 0-3}`
   - Pemetaan nilai:
     - **Power**: `1` (ON), `0` (OFF)
     - **Mode**: `0` (Cool), `1` (Heat), `2` (Auto), `3` (Fan), `4` (Dry)
     - **Wind**: `0` (Auto), `1` (Low), `2` (Medium), `3` (High)

3. **UI Modern Dark Theme ala Windows 11**:
   - Layout elegan dan compact (440x730).
   - Display suhu besar di tengah dengan tombol circular `−` dan `+`.
   - Tombol power utama kontras: `TURN ON` (Glowing Emerald) dan `TURN OFF` (Ruby Red).
   - Quick Presets 1-Klik:
     - ❄️ **Dingin Cepat**: 18°C, Cool, High
     - 💼 **Kerja Santai**: 22°C, Cool, Auto
     - 🌙 **Mode Tidur**: 25°C, Cool, Low
   - Log Box live di bagian bawah yang menampilkan detail request dan raw JSON response dari server Tuya.

4. **Pengaturan Fleksibel & Auto-Persist**:
   - Pilihan Data Center Tuya:
     - Singapore (`https://openapi-sg.iotbing.com`)
     - Western America (`https://openapi.tuyaus.com`)
     - Central Europe (`https://openapi.tuyaeu.com`)
   - Input Client Secret dengan tombol sembunyikan/tampilkan (👁 / 🔒).
   - Tombol **"⇄ Swap"** untuk membalikkan posisi Infrared ID dan Remote ID jika urutan parent/child terbalik.
   - Tombol **"🔑 Test Token"** untuk verifikasi koneksi awal.
   - Konfigurasi tersimpan otomatis ke file [config.json](file:///c:/DISK%20D/Personal%20Project/SmartHome/config.json).

---

## Cara Menjalankan

Buka terminal di folder project dan jalankan:

```bash
dotnet run
```

---

## Struktur File Project

- [SmartHomeAcController.csproj](file:///c:/DISK%20D/Personal%20Project/SmartHome/SmartHomeAcController.csproj): Definisi project .NET 8 WPF.
- [App.xaml](file:///c:/DISK%20D/Personal%20Project/SmartHome/App.xaml): Tema Dark Mode, palette warna, brush, dan control template modern.
- [MainWindow.xaml](file:///c:/DISK%20D/Personal%20Project/SmartHome/MainWindow.xaml): Tampilan UI aplikasi (Window, Expander, Temp Dial, Mode, Wind, Presets, Log).
- [MainWindow.xaml.cs](file:///c:/DISK%20D/Personal%20Project/SmartHome/MainWindow.xaml.cs): Logika event handling, sinkronisasi state UI, dan pengiriman perintah.
- [TuyaClient.cs](file:///c:/DISK%20D/Personal%20Project/SmartHome/TuyaClient.cs): Implementasi REST API Tuya v2.0, signature HMAC-SHA256, token caching, dan endpoint IR Blaster.
- [AppConfig.cs](file:///c:/DISK%20D/Personal%20Project/SmartHome/AppConfig.cs): Penyimpanan dan pemuatan konfigurasi lokal.
- [config.json](file:///c:/DISK%20D/Personal%20Project/SmartHome/config.json): File konfigurasi lokal yang menyimpan kredensial dan state terakhir.
