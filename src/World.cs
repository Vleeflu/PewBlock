using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>A solid axis-aligned box in the map.</summary>
public readonly record struct Block(BoundingBox Box, Color Color);

/// <summary>
/// A map: a list of solid boxes, plus spawn points. Collision, line of sight
/// and bullets all work by testing against these boxes.
/// Two maps exist: the 80 x 80 m arena (deathmatch, parkour) and the
/// 180 x 180 m battle royale map.
/// </summary>
public sealed class World(float halfSize, Color floorColor, Color floorTileColor)
{
    /// <summary>Half the side length: the playable floor spans -HalfSize..HalfSize on X and Z.</summary>
    public float HalfSize { get; } = halfSize;

    public readonly List<Block> Blocks = [];
    public readonly List<Vector3> SpawnPoints = [];

    static readonly Color SandColor = new(201, 196, 176, 255);
    static readonly Color SandTileColor = new(186, 181, 160, 255);
    static readonly Color GrassColor = new(118, 158, 92, 255);
    static readonly Color GrassTileColor = new(108, 148, 84, 255);
    static readonly Color WallColor = new(122, 146, 176, 255);
    static readonly Color TowerColor = new(236, 204, 150, 255);
    static readonly Color CrateColor = new(186, 128, 78, 255);
    static readonly Color PlatformColor = new(150, 186, 206, 255);
    static readonly Color AccentColor = new(226, 112, 92, 255);
    static readonly Color StairColor = new(214, 214, 214, 255);
    static readonly Color RuinColor = new(196, 176, 150, 255);
    static readonly Color RockColor = new(140, 140, 136, 255);
    static readonly Color TrunkColor = new(110, 80, 56, 255);
    static readonly Color LeafColor = new(72, 128, 64, 255);
    static readonly Color SandbagColor = new(170, 156, 116, 255);

    public const float StepRise = 0.5f;
    const float StepDepth = 0.6f;

    /// <summary>Deathmatch and parkour map: 80 x 80 m, central tower, four corner platforms, lanes.</summary>
    public static World CreateArena()
    {
        var w = new World(40, SandColor, SandTileColor);
        w.OuterWalls();
        w.CenterTower();

        // Corner platforms (3 m high) reachable by stairs or by parkour over crates.
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            w.Platform(28 * sx, 28 * sz, sx, sz);
            w.SpawnPoints.Add(new Vector3(28 * sx, 3.01f, 28 * sz));
        }

        // Scattered crates: 1.2 m ones can be jumped on, 2.4 m ones are full cover.
        (float x, float z, float h)[] crates =
        [
            (10, 24, 1.2f), (-10, 24, 2.4f), (24, -10, 1.2f), (24, 10, 2.4f),
            (-24, -10, 2.4f), (-24, 10, 1.2f), (10, -24, 2.4f), (-10, -24, 1.2f),
            (12, 12, 1.2f), (-12, -12, 1.2f), (12, -12, 2.4f), (-12, 12, 2.4f),
            (34, 0, 1.2f), (-34, 0, 1.2f), (0, 34, 1.2f), (0, -34, 1.2f),
        ];
        foreach (var (x, z, h) in crates) w.Box(x, 0, z, 2, h, 2, CrateColor);

        // Ground spawns.
        foreach (int s in new[] { -1, 1 })
        {
            w.SpawnPoints.Add(new Vector3(37 * s, 0.01f, 0));
            w.SpawnPoints.Add(new Vector3(0, 0.01f, 37 * s));
            w.SpawnPoints.Add(new Vector3(14 * s, 0.01f, 34));
            w.SpawnPoints.Add(new Vector3(14 * s, 0.01f, -34));
        }
        return w;
    }

    /// <summary>
    /// Battle royale map: 180 x 180 m of grass. The arena's central tower in the middle,
    /// a ruined town in each corner, a watchtower outpost on each side, and trees, rocks
    /// and crates scattered in between. The scatter uses a fixed seed, so the map is
    /// the same every match.
    /// </summary>
    public static World CreateRoyale()
    {
        var w = new World(90, GrassColor, GrassTileColor);
        w.OuterWalls();
        w.CenterTower();
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
            w.Town(55 * sx, 55 * sz, sx, sz);
        w.Outpost(60, 0, -1, 0);
        w.Outpost(-60, 0, 1, 0);
        w.Outpost(0, 60, 0, -1);
        w.Outpost(0, -60, 0, 1);

        // 16 spawns: town squares, inner ring, and the open ground between outposts and towns.
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            w.SpawnPoints.Add(new Vector3(55 * sx, 0.01f, 55 * sz));
            w.SpawnPoints.Add(new Vector3(30 * sx, 0.01f, 30 * sz));
            w.SpawnPoints.Add(new Vector3(75 * sx, 0.01f, 20 * sz));
            w.SpawnPoints.Add(new Vector3(20 * sx, 0.01f, 75 * sz));
        }

        var rng = new Random(1337);
        w.Scatter(rng, 45, 3.5f, (x, z) => w.Tree(x, z));
        w.Scatter(rng, 35, 3f, (x, z) =>
        {
            float width = 2 + rng.NextSingle() * 2.5f, depth = 2 + rng.NextSingle() * 2.5f;
            float height = rng.Next(3) == 0 ? 1.2f : 1.6f + rng.NextSingle() * 1.6f;
            w.Box(x, 0, z, width, height, depth, RockColor);
        });
        w.Scatter(rng, 30, 3.5f, (x, z) =>
        {
            w.Box(x, 0, z, 2, 1.2f, 2, CrateColor);
            if (rng.Next(2) == 0) w.Box(x + 2, 0, z, 2, 2.4f, 2, CrateColor);
        });
        return w;
    }

    void OuterWalls()
    {
        float s = HalfSize;
        Box(0, -1, 0, s * 2, 1, s * 2, floorColor);
        Box(0, 0, s + 0.5f, s * 2 + 2, 8, 1, WallColor);
        Box(0, 0, -s - 0.5f, s * 2 + 2, 8, 1, WallColor);
        Box(s + 0.5f, 0, 0, 1, 8, s * 2, WallColor);
        Box(-s - 0.5f, 0, 0, 1, 8, s * 2, WallColor);
    }

    /// <summary>4 m tower with stairs on two sides and low cover on top, plus four mid walls that split the area into lanes.</summary>
    void CenterTower()
    {
        Box(0, 0, 0, 10, 4, 10, TowerColor);
        Stairs(0, 5, 0, 1, 8, 3);
        Stairs(0, -5, 0, -1, 8, 3);
        Box(-4.6f, 4, 0, 0.8f, 1.2f, 6, AccentColor);
        Box(4.6f, 4, 0, 0.8f, 1.2f, 6, AccentColor);
        foreach (int s in new[] { -1, 1 })
        {
            Box(18 * s, 0, 0, 1, 3.5f, 16, WallColor);
            Box(0, 0, 18 * s, 16, 3.5f, 1, WallColor);
        }
    }

    /// <summary>12 x 12 m platform, 3 m high, centered at (cx, cz). (sx, sz) point away from the map center.</summary>
    void Platform(float cx, float cz, int sx, int sz)
    {
        Box(cx, 0, cz, 12, 3, 12, PlatformColor);
        Stairs(cx - 6 * sx, cz, -sx, 0, 6, 3);
        Box(cx, 3, cz - 5.6f * sz, 6, 1.2f, 0.8f, AccentColor);   // cover on the edge
        Box(cx + 4 * sx, 0, cz - 7 * sz, 2, 2.4f, 2, CrateColor);  // parkour: 2.4 m crate
        Box(cx + 4 * sx, 0, cz - 9 * sz, 2, 1.2f, 2, CrateColor);  // parkour: 1.2 m crate
    }

    /// <summary>
    /// Three ruined buildings and a platform around a small square at (cx, cz).
    /// (sx, sz) point away from the map center; the platform takes the outer corner.
    /// </summary>
    void Town(float cx, float cz, int sx, int sz)
    {
        Platform(cx + 11 * sx, cz + 11 * sz, sx, sz);
        Ruin(cx - 11 * sx, cz + 11 * sz);
        Ruin(cx + 11 * sx, cz - 11 * sz);
        Ruin(cx - 11 * sx, cz - 11 * sz);
        Box(cx - 3 * sx, 0, cz + 4 * sz, 2, 1.2f, 2, CrateColor);
        Box(cx + 4 * sx, 0, cz - 3 * sz, 2, 2.4f, 2, CrateColor);
    }

    /// <summary>
    /// 10 x 10 m roofless building. Every wall has a 3 m doorway in the middle, so you
    /// (and the bots, who walk in straight lines) can't get trapped inside. Some wall
    /// pieces are broken down to 1.3 m, low enough to jump over.
    /// </summary>
    void Ruin(float cx, float cz)
    {
        const float size = 10, thick = 0.6f, door = 3;
        const float piece = (size - door) / 2, offset = door / 2 + piece / 2, edge = size / 2 - thick / 2;
        int k = 0;
        foreach (int s in new[] { -1, 1 })
        foreach (int p in new[] { -1, 1 })
        {
            float h1 = (k++ % 3 == 0) ? 1.3f : 3.2f;
            float h2 = (k++ % 4 == 0) ? 1.3f : 3.2f;
            Box(cx + p * offset, 0, cz + s * edge, piece, h1, thick, RuinColor); // north / south
            Box(cx + s * edge, 0, cz + p * offset, thick, h2, piece, RuinColor); // east / west
        }
        Box(cx + 2.5f, 0, cz + 2.5f, 2, 1.2f, 2, CrateColor);
    }

    /// <summary>6 m watchtower at (cx, cz) with stairs going toward (dx, dz), sandbags on the other three sides.</summary>
    void Outpost(float cx, float cz, int dx, int dz)
    {
        Box(cx, 0, cz, 4, 6, 4, TowerColor);
        Stairs(cx + dx * 2, cz + dz * 2, dx, dz, 12, 2.4f);
        // Low cover on top, on the three sides without stairs.
        (int x, int z)[] sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        foreach (var (x, z) in sides)
        {
            if (x == dx && z == dz) continue;
            Box(cx + x * 1.8f, 6, cz + z * 1.8f, x != 0 ? 0.4f : 4, 1.1f, z != 0 ? 0.4f : 4, AccentColor);
            Box(cx + x * 7, 0, cz + z * 7, x != 0 ? 1.2f : 8, 1.2f, z != 0 ? 1.2f : 8, SandbagColor);
        }
    }

    /// <summary>Trunk with a two-layer canopy. The canopy is solid, so it blocks shots and sight.</summary>
    void Tree(float x, float z)
    {
        Box(x, 0, z, 0.7f, 3.2f, 0.7f, TrunkColor);
        Box(x, 3.2f, z, 3.4f, 1.6f, 3.4f, LeafColor);
        Box(x, 4.8f, z, 2.2f, 1.1f, 2.2f, MathUtil.Shade(LeafColor, 1.15f));
    }

    /// <summary>
    /// Places <paramref name="count"/> things at random open spots: at least
    /// <paramref name="clearance"/> m from every existing block and 5 m from every spawn.
    /// </summary>
    void Scatter(Random rng, int count, float clearance, Action<float, float> place)
    {
        float limit = HalfSize - 5;
        for (int placed = 0, tries = 0; placed < count && tries < count * 50; tries++)
        {
            float x = (rng.NextSingle() * 2 - 1) * limit, z = (rng.NextSingle() * 2 - 1) * limit;
            var area = new BoundingBox(new Vector3(x - clearance, 0.01f, z - clearance), new Vector3(x + clearance, 6, z + clearance));
            if (Overlaps(area)) continue;
            if (SpawnPoints.Any(sp => MathF.Abs(sp.X - x) < 5 && MathF.Abs(sp.Z - z) < 5)) continue;
            place(x, z);
            placed++;
        }
    }

    /// <summary>Adds a box. (x, z) is the center on the ground, y is the bottom.</summary>
    void Box(float x, float y, float z, float width, float height, float depth, Color color) =>
        Blocks.Add(new Block(new BoundingBox(
            new Vector3(x - width / 2, y, z - depth / 2),
            new Vector3(x + width / 2, y + height, z + depth / 2)), color));

    /// <summary>
    /// Builds stairs against a face at (edgeX, edgeZ) going outward along (dirX, dirZ).
    /// The step touching the face is the highest: steps * StepRise.
    /// </summary>
    void Stairs(float edgeX, float edgeZ, int dirX, int dirZ, int steps, float width)
    {
        for (int k = 0; k < steps; k++)
        {
            float along = k * StepDepth + StepDepth / 2;
            float x = edgeX + dirX * along, z = edgeZ + dirZ * along;
            float w = dirX != 0 ? StepDepth : width;
            float d = dirX != 0 ? width : StepDepth;
            Box(x, 0, z, w, (steps - k) * StepRise, d, StairColor);
        }
    }

    public bool Overlaps(BoundingBox box)
    {
        foreach (var b in Blocks)
            if (Intersects(box, b.Box)) return true;
        return false;
    }

    /// <summary>Strict overlap: boxes that only touch do not count.</summary>
    public static bool Intersects(BoundingBox a, BoundingBox b) =>
        a.Min.X < b.Max.X && a.Max.X > b.Min.X &&
        a.Min.Y < b.Max.Y && a.Max.Y > b.Min.Y &&
        a.Min.Z < b.Max.Z && a.Max.Z > b.Min.Z;

    /// <summary>Finds the nearest block hit by the ray within maxDistance.</summary>
    public bool Raycast(Ray ray, float maxDistance, out RayCollision hit)
    {
        hit = default;
        float best = maxDistance;
        bool any = false;
        foreach (var b in Blocks)
        {
            var c = Raylib.GetRayCollisionBox(ray, b.Box);
            if (c.Hit && c.Distance < best)
            {
                best = c.Distance;
                hit = c;
                any = true;
            }
        }
        return any;
    }

    public bool LineOfSight(Vector3 from, Vector3 to)
    {
        var delta = to - from;
        float length = delta.Length();
        if (length < 0.001f) return true;
        return !Raycast(new Ray(from, delta / length), length, out _);
    }

    /// <summary>Random walkable spot on the ground floor (used as a bot wander target).</summary>
    public Vector3 RandomGroundPoint()
    {
        float limit = HalfSize - 4;
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector3(MathUtil.Range(-limit, limit), 0.01f, MathUtil.Range(-limit, limit));
            var box = new BoundingBox(p + new Vector3(-0.6f, 0, -0.6f), p + new Vector3(0.6f, 1.9f, 0.6f));
            if (!Overlaps(box)) return p;
        }
        return SpawnPoints[Random.Shared.Next(SpawnPoints.Count)];
    }

    /// <summary>Random walkable ground spot inside a circle (bot wander target inside the battle royale zone).</summary>
    public Vector3 RandomGroundPoint(Vector2 center, float radius)
    {
        float limit = HalfSize - 4;
        radius = MathF.Max(radius, 3f);
        for (int i = 0; i < 60; i++)
        {
            float angle = MathUtil.Range(0, MathF.Tau);
            float r = MathF.Sqrt(Random.Shared.NextSingle()) * radius;
            var p = new Vector3(
                Math.Clamp(center.X + MathF.Cos(angle) * r, -limit, limit), 0.01f,
                Math.Clamp(center.Y + MathF.Sin(angle) * r, -limit, limit));
            var box = new BoundingBox(p + new Vector3(-0.6f, 0, -0.6f), p + new Vector3(0.6f, 1.9f, 0.6f));
            if (!Overlaps(box)) return p;
        }
        return SpawnPoints.MinBy(s => Vector2.Distance(new Vector2(s.X, s.Z), center));
    }

    public void Draw()
    {
        foreach (var b in Blocks) Render.ShadedBox(b.Box.Min, b.Box.Max, b.Color);

        // Checkerboard floor tiles give a sense of speed when moving.
        const float tile = 4f;
        int n = (int)(HalfSize / tile);
        for (int i = -n; i < n; i++)
        for (int j = -n; j < n; j++)
            if (((i + j) & 1) == 0)
                Render.FloorQuad(i * tile, j * tile, (i + 1) * tile, (j + 1) * tile, 0.005f, floorTileColor);

        // Dark outlines make the blocky shapes readable.
        foreach (var b in Blocks) Raylib.DrawBoundingBox(b.Box, MathUtil.Shade(b.Color, 0.6f));
    }
}
