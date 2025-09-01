# QueueSystem

Aplikasi Antrian Loket (Server + TellerApp + DisplayApp) berbasis .NET 8 (Windows Only).

## Komponen
- QueueServer.Api : REST API + SignalR + SQLite + Serilog
- QueueServer.Core : Model, Services, Utilities (NumberToBahasa, TicketService)
- Shared : Enum / Kontrak event
- TellerApp.Wpf : Aplikasi Teller (panggil, mulai layani, selesai, skip, ubah settings)
- DisplayApp.Wpf : Tampilan TV fullscreen (nomor dipanggil, next, running text, video, logo, TTS, ESC untuk keluar)

## Persyaratan
- .NET 8 SDK
- Windows (karena WPF + System.Speech)
- Codec video standar (MP4 H.264) untuk MediaElement

## Menjalankan (Dev)
```bash
dotnet restore
dotnet build
cd QueueServer.Api
dotnet run
```
Secara default server di http://localhost:5000 (Kestrel default).  
Jika perlu port lain jalankan:
```bash
dotnet run --urls "http://localhost:5050"
```
Lalu:
- Jalankan TellerApp.Wpf (ubah BaseAddress kalau ganti port)
- Jalankan DisplayApp.Wpf

## Publish (Single File)
Jalankan script:
```powershell
.\build_publish.ps1
```
Output:
```
/publish/server
/publish/teller
/publish/display
```

## Konfigurasi yang Umum Diubah
1. Prefix nomor: via TellerApp menu (disimpan di tabel Settings).
2. Running text: via TellerApp.
3. LogoPath / VideoPath: PUT /api/settings atau lewat tool API (bisa ditambahkan UI nanti).
4. Port server: jalankan server dengan --urls.
5. Chime file: ganti Resources/chime.wav (sama nama file).

## Perubahan Setting Manual (Contoh)
```bash
curl -X PUT http://localhost:5000/api/settings -H "Content-Type: application/json" -d "{\"LogoPath\":\"Resources\\\\logo.png\"}"
```

## Reset Harian
Sequence otomatis berdasarkan Date. Tidak perlu hapus data lama.

## Logging
- Server: Logs/server-*.log
- Teller: Logs/teller-*.log
- Display: Logs/display-*.log

## Catatan Enum
API mengembalikan nilai enum TicketStatus sebagai string uppercase (contoh: "WAITING", "CALLING", "SERVING") 
bukan sebagai angka. Hal ini memudahkan client-side filtering tanpa perlu konversi enum. 
Konfigurasi JsonStringEnumConverter telah diterapkan di Program.cs.

## TODO / Pengembangan Lanjut (Opsional)
- User login & roles
- Multi kategori antrian
- Export CSV
- Printer thermal
- Auto NO_SHOW timer
- Pre-cached TTS

## Folder Resources
Isi file:
- logo.png (placeholder)
- chime.wav (bunyi pendek)
- info.mp4 (video informasi)

Pastikan file benar ada di path relatif jika ingin dipakai Display.

## Troubleshooting
- TTS tidak berbunyi: pastikan Windows Speech engine tersedia (System.Speech).
- Video tidak jalan: coba format MP4 H.264 baseline.
- Hub tidak connect: cek firewall/port; pastikan URL sama di WPF dan API.

Selamat mencoba.