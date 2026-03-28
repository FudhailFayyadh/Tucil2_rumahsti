# Voxelizer 3D - Tucil2 IF2211

Program konversi model 3D dari format `.obj` biasa menjadi model `.obj` berbasis **voxel** (kubus-kubus kecil) menggunakan struktur data **Octree** dan algoritma **Divide and Conquer**.

Dibevelkan dalam bahasa **C#** (.NET 8).

---

## Deskripsi Singkat

Program membaca file `.obj`, membangun Octree dari permukaan model secara rekursif menggunakan algoritma Divide and Conquer, lalu menghasilkan file `.obj` baru di mana setiap leaf node pada kedalaman maksimum merepresentasikan satu voxel (kubus kecil dengan rusuk sama panjang).

---

## Requirement

- **.NET 8 SDK** atau **.NET 8 Runtime**
  - Download: https://dotnet.microsoft.com/download/dotnet/8.0
- Sistem operasi: **Windows** atau **Linux**
- Tidak memerlukan library eksternal (hanya .NET standard library)

---

## Cara Kompilasi

### Linux
```bash
cd src
dotnet build -c Release
```

### Windows
```cmd
cd src
dotnet build -c Release
```

---

## Cara Menjalankan

### Menggunakan `dotnet run` (dari folder src)
```bash
cd src
dotnet run -- <path-file.obj> <kedalaman-maksimum>
```

### Menggunakan DLL (dari folder bin)
```bash
dotnet bin/Voxelizer.dll <path-file.obj> <kedalaman-maksimum>
```

**Contoh:**
```bash
dotnet run -- model.obj 5
dotnet bin/Voxelizer.dll model.obj 5
```

**Parameter:**

| Parameter | Keterangan |
|---|---|
| `path-file.obj` | Path ke file .obj yang ingin dikonversi |
| `kedalaman-maksimum` | Kedalaman maksimum octree (bilangan bulat ≥ 1). Semakin besar = semakin detail |

**Output:**
- File `<nama-input>-voxelized.obj` disimpan di direktori yang sama dengan file input
- Laporan CLI: jumlah voxel, vertex, faces, statistik octree per depth, waktu proses

---

## Panduan Kedalaman

| Kedalaman | Perkiraan Voxel | Cocok untuk |
|---|---|---|
| 3–4 | ~100–500 | Test cepat |
| 5–6 | ~1.000–10.000 | Model sederhana |
| 7–8 | ~10.000–100.000 | Model detail |

---

## Struktur Repository

```
Tucil2_18223123_18223121/
├── doc/
│   └── Laporan_Tucil2_18223123_18223121.pdf
├── src/
│   ├── Program.cs
│   └── Voxelizer.csproj
├── test/
│   ├── cube.obj
│   ├── cube-voxelized.obj
│   └── ...
└── README.md
```

---

## Author

| Nama | NIM |
|---|---|
| Harfhan Ikhtiar Ahmad Rizky | 18223123 |
| Fudhail Fayyadh | 18223121 |
