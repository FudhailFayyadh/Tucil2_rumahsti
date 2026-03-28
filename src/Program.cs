using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

// STRUKTUR DATA
struct Vec3
{
    public double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
}

struct Triangle
{
    public Vec3 A, B, C;
    public Triangle(Vec3 a, Vec3 b, Vec3 c) { A = a; B = b; C = c; }
}

struct AABB
{
    public Vec3 Min, Max;
    public AABB(Vec3 min, Vec3 max) { Min = min; Max = max; }

    public Vec3 Center() => new Vec3(
        (Min.X + Max.X) / 2,
        (Min.Y + Max.Y) / 2,
        (Min.Z + Max.Z) / 2
    );

    public AABB[] Subdivide()
    {
        Vec3 c = Center();
        return new AABB[8]
        {
            new AABB(new Vec3(Min.X, Min.Y, Min.Z), new Vec3(c.X,   c.Y,   c.Z)),
            new AABB(new Vec3(c.X,   Min.Y, Min.Z), new Vec3(Max.X, c.Y,   c.Z)),
            new AABB(new Vec3(Min.X, c.Y,   Min.Z), new Vec3(c.X,   Max.Y, c.Z)),
            new AABB(new Vec3(c.X,   c.Y,   Min.Z), new Vec3(Max.X, Max.Y, c.Z)),
            new AABB(new Vec3(Min.X, Min.Y, c.Z),   new Vec3(c.X,   c.Y,   Max.Z)),
            new AABB(new Vec3(c.X,   Min.Y, c.Z),   new Vec3(Max.X, c.Y,   Max.Z)),
            new AABB(new Vec3(Min.X, c.Y,   c.Z),   new Vec3(c.X,   Max.Y, Max.Z)),
            new AABB(new Vec3(c.X,   c.Y,   c.Z),   new Vec3(Max.X, Max.Y, Max.Z)),
        };
    }
}

class OctreeNode
{
    public AABB Bounds;
    public OctreeNode?[] Children = new OctreeNode?[8];
    public bool IsLeaf = false;
    public bool HasSurface = false;
    public int Depth;
    public OctreeNode(AABB bounds, int depth) { Bounds = bounds; Depth = depth; }
}

class Mesh
{
    public List<Vec3> Vertices = new();
    public List<Triangle> Triangles = new();
}

class Stats
{
    public Dictionary<int, int> NodesByDepth  = new();
    public Dictionary<int, int> PrunedByDepth = new();
    public int MaxDepth;
}

// GEOMETRI - HELPER
static class Geometry
{
    public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X
    );

    public static double Dot(Vec3 a, Vec3 b) => a.X*b.X + a.Y*b.Y + a.Z*b.Z;

    // cek apakah segitiga berpotongan dengan AABB
    public static bool TriangleIntersectsAABB(Triangle tri, AABB box)
    {
        Vec3 c  = box.Center();
        double hx = (box.Max.X - box.Min.X) / 2;
        double hy = (box.Max.Y - box.Min.Y) / 2;
        double hz = (box.Max.Z - box.Min.Z) / 2;

        Vec3 v0 = new Vec3(tri.A.X - c.X, tri.A.Y - c.Y, tri.A.Z - c.Z);
        Vec3 v1 = new Vec3(tri.B.X - c.X, tri.B.Y - c.Y, tri.B.Z - c.Z);
        Vec3 v2 = new Vec3(tri.C.X - c.X, tri.C.Y - c.Y, tri.C.Z - c.Z);

        Vec3 e0 = new Vec3(v1.X-v0.X, v1.Y-v0.Y, v1.Z-v0.Z);
        Vec3 e1 = new Vec3(v2.X-v1.X, v2.Y-v1.Y, v2.Z-v1.Z);
        Vec3 e2 = new Vec3(v0.X-v2.X, v0.Y-v2.Y, v0.Z-v2.Z);

        // 9 sumbu dari cross product sisi segitiga x sumbu AABB
        double[,] axes = {
            {0,-e0.Z, e0.Y}, {0,-e1.Z, e1.Y}, {0,-e2.Z, e2.Y},
            {e0.Z,0,-e0.X},  {e1.Z,0,-e1.X},  {e2.Z,0,-e2.X},
            {-e0.Y,e0.X,0},  {-e1.Y,e1.X,0},  {-e2.Y,e2.X,0}
        };

        for (int i = 0; i < 9; i++)
        {
            double ax = axes[i,0], ay = axes[i,1], az = axes[i,2];
            double p0 = ax*v0.X + ay*v0.Y + az*v0.Z;
            double p1 = ax*v1.X + ay*v1.Y + az*v1.Z;
            double p2 = ax*v2.X + ay*v2.Y + az*v2.Z;
            double r  = hx*Math.Abs(ax) + hy*Math.Abs(ay) + hz*Math.Abs(az);
            if (Math.Min(p0,Math.Min(p1,p2)) > r || Math.Max(p0,Math.Max(p1,p2)) < -r) return false;
        }

        // 3 sumbu AABB
        if (Math.Max(v0.X,Math.Max(v1.X,v2.X)) < -hx || Math.Min(v0.X,Math.Min(v1.X,v2.X)) > hx) return false;
        if (Math.Max(v0.Y,Math.Max(v1.Y,v2.Y)) < -hy || Math.Min(v0.Y,Math.Min(v1.Y,v2.Y)) > hy) return false;
        if (Math.Max(v0.Z,Math.Max(v1.Z,v2.Z)) < -hz || Math.Min(v0.Z,Math.Min(v1.Z,v2.Z)) > hz) return false;

        // Sumbu normal segitiga
        Vec3   normal = Cross(e0, e1);
        double d      = Dot(normal, v0);
        double r2     = hx*Math.Abs(normal.X) + hy*Math.Abs(normal.Y) + hz*Math.Abs(normal.Z);
        if (d > r2 || d < -r2) return false;

        return true;
    }
}

// PARSER .OBJ
static class ObjParser
{
    public static Mesh Parse(string path)
    {
        var mesh   = new Mesh();
        int lineNum = 0;

        foreach (var rawLine in File.ReadLines(path))
        {
            lineNum++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v":
                    if (parts.Length < 4)
                        throw new Exception($"Baris {lineNum}: format vertex tidak valid (butuh x y z)");
                    if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double vx) ||
                        !double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double vy) ||
                        !double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double vz))
                        throw new Exception($"Baris {lineNum}: nilai koordinat tidak valid");
                    mesh.Vertices.Add(new Vec3(vx, vy, vz));
                    break;

                case "f":
                    if (parts.Length < 4)
                        throw new Exception($"Baris {lineNum}: format face tidak valid (butuh minimal 3 indeks)");

                    int ParseIdx(string s)
                    {
                        string raw = s.Split('/')[0];
                        if (!int.TryParse(raw, out int idx))
                            throw new Exception($"Baris {lineNum}: indeks face tidak valid '{s}'");
                        return idx;
                    }

                    int i0 = ParseIdx(parts[1]);
                    int i1 = ParseIdx(parts[2]);
                    int i2 = ParseIdx(parts[3]);
                    int n  = mesh.Vertices.Count;

                    if (i0 < 1 || i1 < 1 || i2 < 1 || i0 > n || i1 > n || i2 > n)
                        throw new Exception($"Baris {lineNum}: indeks face di luar batas (total vertex: {n})");

                    mesh.Triangles.Add(new Triangle(
                        mesh.Vertices[i0-1], mesh.Vertices[i1-1], mesh.Vertices[i2-1]));

                    // Fan triangulation untuk face > 3 vertex
                    for (int k = 4; k < parts.Length; k++)
                    {
                        int ik  = ParseIdx(parts[k]);
                        int iprev = ParseIdx(parts[k-1]);
                        if (ik < 1 || ik > n || iprev < 1 || iprev > n)
                            throw new Exception($"Baris {lineNum}: indeks face di luar batas");
                        mesh.Triangles.Add(new Triangle(
                            mesh.Vertices[i0-1], mesh.Vertices[iprev-1], mesh.Vertices[ik-1]));
                    }
                    break;

                default:
                    // Abaikan baris lain: vt, vn, usemtl, mtllib, s, o, g, dll
                    break;
            }
        }

        if (mesh.Vertices.Count == 0)  throw new Exception("File .obj tidak memiliki vertex");
        if (mesh.Triangles.Count == 0) throw new Exception("File .obj tidak memiliki faces");

        return mesh;
    }
}

// OCTREE - DIVIDE AND CONQUER
static class OctreeBuilder
{
    public static AABB ComputeBounds(Mesh mesh)
    {
        double minX=double.MaxValue, minY=double.MaxValue, minZ=double.MaxValue;
        double maxX=double.MinValue, maxY=double.MinValue, maxZ=double.MinValue;

        foreach (var v in mesh.Vertices)
        {
            if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
            if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
            if (v.Z < minZ) minZ = v.Z; if (v.Z > maxZ) maxZ = v.Z;
        }

        double cx   = (minX + maxX) / 2;
        double cy   = (minY + maxY) / 2;
        double cz   = (minZ + maxZ) / 2;
        double half = Math.Max(maxX-minX, Math.Max(maxY-minY, maxZ-minZ)) / 2 * 1.001;

        return new AABB(new Vec3(cx-half,cy-half,cz-half), new Vec3(cx+half,cy+half,cz+half));
    }

    // Rekursi Divide and Conquer
    public static OctreeNode Build(AABB bounds, List<Triangle> triangles, int depth, int maxDepth, Stats stats)
    {
        var node = new OctreeNode(bounds, depth);

        if (!stats.NodesByDepth.ContainsKey(depth))  stats.NodesByDepth[depth]  = 0;
        if (!stats.PrunedByDepth.ContainsKey(depth)) stats.PrunedByDepth[depth] = 0;
        stats.NodesByDepth[depth]++;

        // Tidak ada segitiga -> pruning
        if (triangles.Count == 0)
        {
            stats.PrunedByDepth[depth]++;
            node.IsLeaf = true;
            node.HasSurface = false;
            return node;
        }

        // Kedalaman maksimum: jadikan voxel
        if (depth == maxDepth)
        {
            node.IsLeaf = true;
            node.HasSurface = true;
            return node;
        }

        // bagi menjadi 8 oktan
        AABB[] subBoxes = bounds.Subdivide();
        bool anyChild = false;

        for (int i = 0; i < 8; i++)
        {
            // filter segitiga yang berpotongan dengan oktan ini
            var childTris = new List<Triangle>();
            foreach (var tri in triangles)
                if (Geometry.TriangleIntersectsAABB(tri, subBoxes[i]))
                    childTris.Add(tri);

            if (childTris.Count == 0)
            {
                // Pangkas oktan kosong
                if (!stats.NodesByDepth.ContainsKey(depth+1))  stats.NodesByDepth[depth+1]  = 0;
                if (!stats.PrunedByDepth.ContainsKey(depth+1)) stats.PrunedByDepth[depth+1] = 0;
                stats.NodesByDepth[depth+1]++;
                stats.PrunedByDepth[depth+1]++;
                continue;
            }

            node.Children[i] = Build(subBoxes[i], childTris, depth+1, maxDepth, stats);
            anyChild = true;
        }

        if (!anyChild) { node.IsLeaf = true; node.HasSurface = true; }
        return node;
    }

    public static void CollectLeaves(OctreeNode? node, List<OctreeNode> leaves)
    {
        if (node == null) return;
        if (node.IsLeaf) { if (node.HasSurface) leaves.Add(node); return; }
        foreach (var child in node.Children)
            CollectLeaves(child, leaves);
    }
}

// GENERATOR OUTPUT .OBJ
static class VoxelExporter
{
    static readonly double[,] CubeVerts = {
        {0,0,0},{1,0,0},{1,0,1},{0,0,1},
        {0,1,0},{1,1,0},{1,1,1},{0,1,1}
    };
    static readonly int[,] CubeFaces = {
        {0,1,2},{0,2,3},   // bawah
        {4,6,5},{4,7,6},   // atas
        {0,4,5},{0,5,1},   // depan
        {2,6,7},{2,7,3},   // belakang
        {0,3,7},{0,7,4},   // kiri
        {1,5,6},{1,6,2}    // kanan
    };

    public static (string content, int voxels, int vertices, int faces) Generate(List<OctreeNode> leaves)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Voxelized OBJ - generated by Voxelizer C#");

        int offset = 0, totalV = 0, totalF = 0;

        foreach (var leaf in leaves)
        {
            Vec3   bMin = leaf.Bounds.Min;
            Vec3   bMax = leaf.Bounds.Max;
            double side = Math.Min(bMax.X-bMin.X, Math.Min(bMax.Y-bMin.Y, bMax.Z-bMin.Z));

            for (int v = 0; v < 8; v++)
            {
                double x = bMin.X + CubeVerts[v,0] * side;
                double y = bMin.Y + CubeVerts[v,1] * side;
                double z = bMin.Z + CubeVerts[v,2] * side;
                sb.AppendLine(FormattableString.Invariant($"v {x:F6} {y:F6} {z:F6}"));
            }
            totalV += 8;

            for (int f = 0; f < 12; f++)
            {
                int i0 = offset + CubeFaces[f,0] + 1;
                int i1 = offset + CubeFaces[f,1] + 1;
                int i2 = offset + CubeFaces[f,2] + 1;
                sb.AppendLine($"f {i0} {i1} {i2}");
            }
            totalF  += 12;
            offset  += 8;
        }

        return (sb.ToString(), leaves.Count, totalV, totalF);
    }
}

// PROGRAM UTAMA
class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Penggunaan: Voxelizer <path-file.obj> <kedalaman-maksimum>");
            Console.WriteLine("Contoh    : Voxelizer model.obj 5");
            Environment.Exit(1);
        }

        string inputPath = args[0];

        if (!int.TryParse(args[1], out int maxDepth) || maxDepth < 1)
        {
            Console.WriteLine("Error: kedalaman maksimum harus berupa bilangan bulat positif");
            Environment.Exit(1);
        }

        if (!string.Equals(Path.GetExtension(inputPath), ".obj", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Error: file input harus berformat .obj");
            Environment.Exit(1);
        }

        if (!File.Exists(inputPath))
        {
            Console.WriteLine($"Error: file tidak ditemukan: {inputPath}");
            Environment.Exit(1);
        }

        Console.WriteLine("================================================");
        Console.WriteLine("  VOXELIZER 3D - Berbasis Octree (D&C) [C#]   ");
        Console.WriteLine("================================================");
        Console.WriteLine($"File input     : {inputPath}");
        Console.WriteLine($"Kedalaman maks : {maxDepth}");
        Console.WriteLine();

        // 1. Parse .obj  (di luar timer, sesuai QnA)
        Console.Write("Memuat file .obj ... ");
        Mesh mesh;
        try   { mesh = ObjParser.Parse(inputPath); }
        catch (Exception ex) { Console.WriteLine($"\nError: {ex.Message}"); Environment.Exit(1); return; }
        Console.WriteLine($"OK ({mesh.Vertices.Count} vertex, {mesh.Triangles.Count} segitiga)");

        AABB bounds = OctreeBuilder.ComputeBounds(mesh);
        Console.WriteLine($"Bounding box   : ({bounds.Min.X:F3},{bounds.Min.Y:F3},{bounds.Min.Z:F3})" +
                          $" - ({bounds.Max.X:F3},{bounds.Max.Y:F3},{bounds.Max.Z:F3})");

        // *** TIMER MULAI: setelah .obj selesai di-input ***
        var sw = Stopwatch.StartNew();

        // 2. Bangun Octree
        Console.Write("Membangun Octree ... ");
        var stats = new Stats { MaxDepth = maxDepth };
        OctreeNode root = OctreeBuilder.Build(bounds, mesh.Triangles, 1, maxDepth, stats);
        Console.WriteLine("OK");

        // 3. Kumpulkan leaf voxel
        var leaves = new List<OctreeNode>();
        OctreeBuilder.CollectLeaves(root, leaves);

        // 4. Generate konten .obj
        Console.Write("Menghasilkan file .obj ... ");
        var (objContent, totalVoxels, totalVertices, totalFaces) = VoxelExporter.Generate(leaves);

        // *** TIMER BERHENTI: sebelum file disimpan ***
        sw.Stop();

        // 5. Simpan output
        string baseName   = Path.GetFileNameWithoutExtension(inputPath);
        string dir        = Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".";
        string outputPath = Path.Combine(dir, baseName + "-voxelized.obj");

        try   { File.WriteAllText(outputPath, objContent); }
        catch (Exception ex) { Console.WriteLine($"\nError: {ex.Message}"); Environment.Exit(1); return; }
        Console.WriteLine("OK");

        // ===== LAPORAN =====
        Console.WriteLine();
        Console.WriteLine("================================================");
        Console.WriteLine("  LAPORAN HASIL VOXELISASI");
        Console.WriteLine("================================================");
        Console.WriteLine($"Banyaknya voxel   : {totalVoxels}");
        Console.WriteLine($"Banyaknya vertex  : {totalVertices}");
        Console.WriteLine($"Banyaknya faces   : {totalFaces}");
        Console.WriteLine($"Kedalaman octree  : {maxDepth}");

        Console.WriteLine();
        Console.WriteLine("--- Statistik Node Octree yang Terbentuk ---");
        for (int d = 1; d <= maxDepth; d++)
        {
            stats.NodesByDepth.TryGetValue(d, out int cnt1);
            Console.WriteLine($"  {d} : {cnt1}");
        }

        Console.WriteLine();
        Console.WriteLine("--- Statistik Node yang Tidak Perlu Ditelusuri ---");
        for (int d = 1; d <= maxDepth; d++)
        {
            stats.PrunedByDepth.TryGetValue(d, out int cnt2);
            Console.WriteLine($"  {d} : {cnt2}");
        }

        Console.WriteLine();
        Console.WriteLine($"Lama waktu berjalan : {sw.Elapsed}");
        Console.WriteLine($"File output disimpan: {outputPath}");
        Console.WriteLine("================================================");
    }
}
