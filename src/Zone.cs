using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>
/// The battle royale circle. Each phase waits, then shrinks the circle toward a
/// smaller one placed randomly inside it. Anyone outside takes damage every tick,
/// which forces fights and guarantees the match ends.
/// </summary>
public sealed class Zone
{
    readonly record struct Phase(float Wait, float Shrink, float Radius, float Dps);

    // The last phase closes completely, so the match can't stall (about 2 minutes max).
    static readonly Phase[] Phases =
    [
        new(Wait: 20, Shrink: 20, Radius: 34, Dps: 2),
        new(Wait: 15, Shrink: 15, Radius: 18, Dps: 4),
        new(Wait: 10, Shrink: 15, Radius: 8, Dps: 7),
        new(Wait: 8, Shrink: 15, Radius: 0, Dps: 15),
    ];
    const float StartRadius = 58f; // covers the whole 80 x 80 arena, corners included

    static readonly Color WallColor = new(140, 80, 255, 70);
    static readonly Color NextColor = new(255, 255, 255, 200);

    public Vector2 Center { get; private set; }     // (x, z) on the ground
    public float Radius { get; private set; }
    public Vector2 NextCenter { get; private set; }
    public float NextRadius { get; private set; }
    public bool Shrinking { get; private set; }

    int phase;
    float timer;
    Vector2 fromCenter;
    float fromRadius;

    bool Finished => phase >= Phases.Length;
    public float Dps => Phases[Math.Min(phase, Phases.Length - 1)].Dps;

    public string Status => Finished ? "ZONE CLOSED"
        : Shrinking ? $"ZONE CLOSING {MathF.Ceiling(Phases[phase].Shrink - timer)}s"
        : $"ZONE SHRINKS IN {MathF.Ceiling(Phases[phase].Wait - timer)}s";

    public void Reset()
    {
        Center = Vector2.Zero;
        Radius = StartRadius;
        phase = 0;
        timer = 0;
        Shrinking = false;
        PickNext();
    }

    public bool Contains(Vector3 p) => Vector2.Distance(new Vector2(p.X, p.Z), Center) <= Radius;
    public bool NextContains(Vector3 p) => Vector2.Distance(new Vector2(p.X, p.Z), NextCenter) <= NextRadius;

    public void Update(float dt)
    {
        if (Finished) return;
        var ph = Phases[phase];
        timer += dt;
        if (!Shrinking)
        {
            if (timer < ph.Wait) return;
            Shrinking = true;
            timer = 0;
            fromCenter = Center;
            fromRadius = Radius;
            return;
        }

        float t = MathF.Min(timer / ph.Shrink, 1f);
        Center = Vector2.Lerp(fromCenter, NextCenter, t);
        Radius = float.Lerp(fromRadius, NextRadius, t);
        if (t < 1f) return;

        phase++;
        timer = 0;
        Shrinking = false;
        if (!Finished) PickNext();
    }

    /// <summary>Next circle: random spot that keeps it inside the current circle and the arena.</summary>
    void PickNext()
    {
        NextRadius = Phases[phase].Radius;
        float maxOffset = MathF.Max(0, Radius - NextRadius);
        float angle = MathUtil.Range(0, MathF.Tau);
        float dist = MathF.Sqrt(Random.Shared.NextSingle()) * maxOffset;
        var c = Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * dist;
        float limit = MathF.Max(0, World.HalfSize - NextRadius - 2);
        NextCenter = Vector2.Clamp(c, new Vector2(-limit), new Vector2(limit));
    }

    /// <summary>Translucent wall around the current circle, plus a white ring for the next one.</summary>
    public void Draw()
    {
        if (!Finished) Raylib.DrawCircle3D(new Vector3(NextCenter.X, 0.05f, NextCenter.Y), NextRadius, Vector3.UnitX, 90, NextColor);
        if (Radius < 0.05f) return;

        const int segments = 96;
        const float bottom = -1f, top = 30f;
        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling(); // visible from inside and outside
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(WallColor.R, WallColor.G, WallColor.B, WallColor.A);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * MathF.Tau / segments, a1 = (i + 1) * MathF.Tau / segments;
            float x0 = Center.X + MathF.Cos(a0) * Radius, z0 = Center.Y + MathF.Sin(a0) * Radius;
            float x1 = Center.X + MathF.Cos(a1) * Radius, z1 = Center.Y + MathF.Sin(a1) * Radius;
            Rlgl.Vertex3f(x0, bottom, z0);
            Rlgl.Vertex3f(x1, bottom, z1);
            Rlgl.Vertex3f(x1, top, z1);
            Rlgl.Vertex3f(x0, top, z0);
        }
        Rlgl.End();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableBackfaceCulling();
    }
}
