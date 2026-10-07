using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>A solid axis-aligned box in the map.</summary>
public readonly record struct Block(BoundingBox Box, Color Color);

/// <summary>
/// The map: a list of solid boxes, plus spawn points. Collision, line of sight
/// and bullets all work by testing against these boxes.
/// </summary>
public sealed class World
{
    public const float HalfSize = 40f;

    public readonly List<Block> Blocks = [];
    public readonly List<Vector3> SpawnPoints = [];

    static readonly Color FloorColor = new(201, 196, 176, 255);
    static readonly Color FloorTileColor = new(186, 181, 160, 255);
    static readonly Color WallColor = new(122, 146, 176, 255);
    static readonly Color TowerColor = new(236, 204, 150, 255);
    static readonly Color CrateColor = new(186, 128, 78, 255);
    static readonly Color PlatformColor = new(150, 186, 206, 255);
    static readonly Color AccentColor = new(226, 112, 92, 255);
    static readonly Color StairColor = new(214, 214, 214, 255);

    public const float StepRise = 0.5f;
    const float StepDepth = 0.6f;

    public static World CreateArena()
    {
        var w = new World();

        // Floor and outer walls.
        w.Box(0, -1, 0, HalfSize * 2, 1, HalfSize * 2, FloorColor);
        w.Box(0, 0, HalfSize + 0.5f, HalfSize * 2 + 2, 8, 1, WallColor);
        w.Box(0, 0, -HalfSize - 0.5f, HalfSize * 2 + 2, 8, 1, WallColor);
        w.Box(HalfSize + 0.5f, 0, 0, 1, 8, HalfSize * 2, WallColor);
        w.Box(-HalfSize - 0.5f, 0, 0, 1, 8, HalfSize * 2, WallColor);

        // Central tower (4 m high), stairs on two sides, low cover on top.
        w.Box(0, 0, 0, 10, 4, 10, TowerColor);
        w.Stairs(0, 5, 0, 1, 8, 3);
        w.Stairs(0, -5, 0, -1, 8, 3);
        w.Box(-4.6f, 4, 0, 0.8f, 1.2f, 6, AccentColor);
        w.Box(4.6f, 4, 0, 0.8f, 1.2f, 6, AccentColor);

        // Corner platforms (3 m high) reachable by stairs or by parkour over crates.
        foreach (int sx in new[] { -1, 1 })
        foreach (int sz in new[] { -1, 1 })
        {
            float cx = 28 * sx, cz = 28 * sz;
            w.Box(cx, 0, cz, 12, 3, 12, PlatformColor);
            w.Stairs(cx - 6 * sx, cz, -sx, 0, 6, 3);
            w.Box(cx, 3, cz - 5.6f * sz, 6, 1.2f, 0.8f, AccentColor);   // cover on the edge
            w.Box(cx + 4 * sx, 0, cz - 7 * sz, 2, 2.4f, 2, CrateColor);  // parkour: 2.4 m crate
            w.Box(cx + 4 * sx, 0, cz - 9 * sz, 2, 1.2f, 2, CrateColor);  // parkour: 1.2 m crate
            w.SpawnPoints.Add(new Vector3(cx, 3.01f, cz));
        }

        // Mid walls that split the arena into lanes.
        foreach (int s in new[] { -1, 1 })
        {
            w.Box(18 * s, 0, 0, 1, 3.5f, 16, WallColor);
            w.Box(0, 0, 18 * s, 16, 3.5f, 1, WallColor);
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
        for (int i = 0; i < 40; i++)
        {
            var p = new Vector3(MathUtil.Range(-36, 36), 0.01f, MathUtil.Range(-36, 36));
            var box = new BoundingBox(p + new Vector3(-0.6f, 0, -0.6f), p + new Vector3(0.6f, 1.9f, 0.6f));
            if (!Overlaps(box)) return p;
        }
        return SpawnPoints[Random.Shared.Next(SpawnPoints.Count)];
    }

    /// <summary>Random walkable ground spot inside a circle (bot wander target inside the battle royale zone).</summary>
    public Vector3 RandomGroundPoint(Vector2 center, float radius)
    {
        const float limit = HalfSize - 4;
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
                Render.FloorQuad(i * tile, j * tile, (i + 1) * tile, (j + 1) * tile, 0.005f, FloorTileColor);

        // Dark outlines make the blocky shapes readable.
        foreach (var b in Blocks) Raylib.DrawBoundingBox(b.Box, MathUtil.Shade(b.Color, 0.6f));
    }
}
