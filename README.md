# Tactical FPS Prototype

[![Unity](https://img.shields.io/badge/Unity-6000.0.4f1%20(Unity%206)-black?logo=unity)](https://unity.com/)
[![Render Pipeline](https://img.shields.io/badge/Render%20Pipeline-URP-blue)](https://unity.com/srp/Universal-Render-Pipeline)
[![Input System](https://img.shields.io/badge/Input%20System-New%20Input%20System-orange)](https://docs.unity3d.com/Packages/com.unity.inputsystem@latest)
[![Platform](https://img.shields.io/badge/Platform-Windows%20PC-blue?logo=windows)](https://microsoft.com)
[![Language](https://img.shields.io/badge/Language-C%23-239120?logo=csharp)](https://docs.microsoft.com/dotnet/csharp/)

Prototipe first-person shooter (FPS) taktis berbasis ronde (*round-based*) bergaya Counter-Strike dan Valorant yang dikembangkan menggunakan **Unity 6 (6000.0.4f1)** dan **Universal Render Pipeline (URP)**. 

Proyek ini mengintegrasikan mekanisme penanaman dan penjinakan bom C4 (Site A & Site B), persenjataan multi-slot, granat taktis dengan dual-throw, radar taktis yang berputar mengikuti sudut pandang pemain, serta bot musuh humanoid cerdas yang dilengkapi sistem **Full Physics Ragdoll**.

---

## Fitur Utama

### 1. Multi-Weapon Arsenal & Tactical Switch System
- **Slot 1 - Primary Weapon (SMG-K Tactical)**: Dilengkapi spray pattern khas, ekspansi crosshair dinamis, sentakan recoil prosedural pada kamera, dan selongsong peluru (*casing ejection*).
- **Slot 2 - Secondary Weapon (Pistol-A Semi-Auto)**: Senjata cadangan responsif dengan bidikan presisi tinggi dan lethality instan pada tembakan kepala (*headshot*).
- **Slot 3 - Tactical Frag Grenade**: Mekanika lemparan ganda:
  - **Klik Kiri (Overhand Throw)**: Lemparan jauh berkecepatan 18 m/s dengan fuse 2.2 detik.
  - **Klik Kanan (Underhand Lob)**: Lemparan pendek/lob berkecepatan 8 m/s untuk area sempit atau lemparan di balik sudut tembok.
  - **Area of Effect (AoE)**: Radius ledakan 6.5 meter (100 damage maksimal) yang memicu daya dorong fisika pada objek sekitar.
- **Slot 4 - C4 Explosive**: Bom objektif yang dapat dibawa oleh bot teroris menuju bomb site.

### 2. Humanoid Bot AI & Full Physics Ragdoll
- **State Machine AI**: Bot bergerak secara otonom melalui tahapan state: *Patrol*, *Alert*, *Combat (Firing Burst)*, *Planting C4*, *Defending Bomb*, dan *Dropped Bomb Recovery*.
- **Auto-Rigged Ragdoll Controller**: Sendi tulang model humanoid (Mixamo) secara otomatis dipetakan ke dalam `Rigidbody`, `CapsuleCollider`, dan `CharacterJoint` lengkap dengan batas sudut sendi alami manusia.
- **Dinamika Impuls & Headshot Snapping**:
  - Tembakan peluru menerapkan gaya dorong vektor pada sendi tulang yang terkena dampak.
  - Tembakan kepala memicu efek sentakan rotasi (*head snapping torque*).
  - Ledakan C4 dan granat menerapkan *Explosion Force* yang membuat tubuh bot melayang terpental secara realistis.
- **Seamless Round Reset**: Setiap awal ronde, struktur tulang bot secara otomatis dipulihkan ke posisi semula dan animator di-rebind tanpa perlu reload scene.

### 3. Bomb Defusal Objective (Site A & Site B)
- **Bomb Sites Terkalibrasi**: Area penanaman Site A (Radar Hub) dan Site B (Storage Complex) ditandai dengan pelat hazard lantai bergaris kuning-hitam dan papan nama taktis 3D.
- **Hitung Mundur & Audio Dinamis**: Timer ledakan 45 detik dengan akselerasi bip audio prosedural yang semakin cepat mendekati waktu detonasi.
- **Mekanisme Defuse CT**: Pemain CT dapat menahan tombol `[E]` selama 5.0 detik dalam jarak 2.4 meter untuk menjinakkan bom, dilengkapi bar progress visual pada HUD.
- **Radius Detonasi**: Ledakan C4 berdaya hancur 24 meter dengan efek visual kilatan bola api dan guncangan kamera intens.
- **Dropped Bomb Recovery**: Jika bot pembawa bom tereliminasi di lorong sebelum menanam, bom akan terjatuh ke tanah lengkap dengan beacon light merah. Bot rekan yang masih hidup akan memutar arah untuk mengambil bom dan melanjutkan objektif penanaman.

### 4. Rotating Tactical Radar (Minimap)
- Minimap melingkar di pojok kiri atas yang berputar secara mulus mengikuti orientasi arah hadap pemain (*yaw*).
- Dilengkapi indikator mata angin (N, E, S, W), cincin indikator jarak (15m dan 30m), serta kerucut sudut pandang (*vision cone*).
- **Gunfire Ping System**: Mendeteksi dan menampilkan titik merah posisi musuh ketika mereka melepaskan tembakan.

### 5. Tactical Round Manager & HUD
- Sistem pertandingan *First to 3 Wins* dengan jeda persiapan 4 detik (*Freeze Time*) di awal setiap ronde.
- **HUD Komprehensif**: Menampilkan sisa waktu ronde/bom, indikator skor tim (CT vs T), status slot amunisi [1]-[4], banner eliminasi multi-kill, serta killfeed taktis di pojok kanan atas.

---

## Panduan Kontrol

| Tombol / Input | Aksi |
| --- | --- |
| `W`, `A`, `S`, `D` | Bergerak (Maju, Kiri, Mundur, Kanan) |
| `Mouse Look` | Mengarahkan pandangan / Bidikan senjata |
| `Left Click` | Menembak senjata / Lemparan jauh granat |
| `Right Click` | Lemparan dekat granat (*Underhand Lob*) |
| `1` | Memilih Slot 1 (Primary SMG-K) |
| `2` | Memilih Slot 2 (Secondary Pistol-A) |
| `3` | Memilih Slot 3 (Tactical Frag Grenade) |
| `4` | Memilih Slot 4 (C4 Explosive) |
| `Scroll Wheel` | Mengganti slot senjata ke atas / ke bawah |
| `R` | Mengisi ulang amunisi (*Reload*) |
| `E` (Tahan 5 detik) | Menjinakkan bom C4 (*Defuse*) |
| `Left Shift` (Tahan) | Berjalan pelan (*Walking / Silent step*) |
| `Left Ctrl` (Tahan) | Jongkok (*Crouch*) |
| `Space` | Melompat (*Jump*) |
| `F3` | Menampilkan / Menyembunyikan telemetri fisika kecepatan |

---

## Struktur Arsitektur Skrip

```text
Assets/Scripts/
|-- Combat/
|   |-- EnemyHumanoidAI.cs          # State machine AI bot humanoid dan navigasi
|   |-- HumanoidRagdollController.cs # Sistem fisika ragdoll penuh dan sendi tulang
|   |-- FPSHitbox.cs                # Deteksi hitbox presisi (Head, Body, Limb)
|   |-- PlantedC4.cs                # Logika bom tertanam, timer, audio, dan defuse
|   |-- PlantZone.cs                # Penanda batas volume zona tanam Site A & Site B
|   |-- TacticalArenaBuilder.cs     # Generator prosedural peta arena, cover, dan props
|   |-- TacticalRoundManager.cs     # Alur ronde, freeze time, dan penentuan pemenang
|   |-- ThrownGrenade.cs            # Proyektil granat pecahan, pantulan, dan AoE blast
|-- Player/
|   |-- FPSCameraController.cs      # Kontrol kamera, recoil pitch/yaw, dan view bobbing
|   |-- FPSMovementController.cs    # Fisika pergerakan (akselerasi, gesekan, crouch)
|   |-- FPSWeapon.cs                # Mekanika penembakan, raycast, dan spray pattern
|   |-- FPSWeaponManager.cs         # Manajemen inventori slot senjata dan viewmodel
|   |-- FPSTacticalHUD.cs           # Tampilan antarmuka grafis (HUD, amunisi, timer, defuse)
|   |-- FPSTacticalRadar.cs         # Radar minimap melingkar dan gunfire ping
```

---

## Cara Menjalankan Proyek

1. **Prasyarat**:
   - Pastikan komputer terpasang **Unity 6000.0.4f1** (atau versi Unity 6 yang kompatibel) beserta modul *Windows Build Support*.
   - Git LFS (*Large File Storage*) aktif untuk menangani file 3D model/animasi Mixamo.
2. **Clone Repositori**:
   ```bash
   git clone https://github.com/christianlandrawicaksono-collab/My-FPS-Project.git
   ```
3. **Buka di Unity Hub**:
   - Buka aplikasi **Unity Hub**.
   - Klik **Add > Add project from disk**, lalu pilih folder proyek hasil clone.
   - Buka proyek menggunakan editor Unity 6.
4. **Jalankan Permainan**:
   - Di tab *Project*, buka folder `Assets/Scenes/`.
   - Buka scene **`Training.unity`**.
   - Tekan tombol **Play** di bagian atas Unity Editor untuk memulai pertandingan.
