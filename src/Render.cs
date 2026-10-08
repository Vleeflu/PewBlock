using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>
/// Draws boxes with "fake lighting": each face gets a fixed brightness, so the
/// top is brightest and the bottom darkest. This is the flat, low-poly look
/// Krunker uses, and it costs nothing (no lights, no shaders).
/// </summary>
public static class Render
{
    const float Top = 1.0f, SideX = 0.86f, SideZ = 0.72f, Bottom = 0.55f;

    public static void ShadedCube(Vector3 center, Vector3 size, Color color) =>
        ShadedBox(center - size / 2, center + size / 2, color);

    /// <summary>
    /// Emits 6 quads. Vertices are listed counter-clockwise as seen from outside,
    /// because the GPU culls (skips) faces whose vertices appear clockwise.
    /// </summary>
    public static void ShadedBox(Vector3 a, Vector3 b, Color c)
    {
        Rlgl.Begin((int)DrawMode.Quads);

        SetColor(c, Top); // +Y
        V(a.X, b.Y, b.Z); V(b.X, b.Y, b.Z); V(b.X, b.Y, a.Z); V(a.X, b.Y, a.Z);

        SetColor(c, Bottom); // -Y
        V(a.X, a.Y, a.Z); V(b.X, a.Y, a.Z); V(b.X, a.Y, b.Z); V(a.X, a.Y, b.Z);

        SetColor(c, SideZ); // +Z
        V(a.X, a.Y, b.Z); V(b.X, a.Y, b.Z); V(b.X, b.Y, b.Z); V(a.X, b.Y, b.Z);

        SetColor(c, SideZ); // -Z
        V(b.X, a.Y, a.Z); V(a.X, a.Y, a.Z); V(a.X, b.Y, a.Z); V(b.X, b.Y, a.Z);

        SetColor(c, SideX); // +X
        V(b.X, a.Y, b.Z); V(b.X, a.Y, a.Z); V(b.X, b.Y, a.Z); V(b.X, b.Y, b.Z);

        SetColor(c, SideX); // -X
        V(a.X, a.Y, a.Z); V(a.X, a.Y, b.Z); V(a.X, b.Y, b.Z); V(a.X, b.Y, a.Z);

        Rlgl.End();
    }

    /// <summary>Box rotated around the X axis (positive tips the top forward, toward +Z), e.g. a slanted grip or magazine.</summary>
    public static void TiltedCube(Vector3 center, Vector3 size, float degrees, Color color)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(center.X, center.Y, center.Z);
        Rlgl.Rotatef(degrees, 1, 0, 0);
        ShadedCube(Vector3.Zero, size, color);
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Cylinder from <paramref name="a"/> to <paramref name="b"/> with capped ends. Each side
    /// quad gets the same fake lighting as boxes, picked from the direction it faces.
    /// </summary>
    public static void ShadedCylinder(Vector3 a, Vector3 b, float radius, Color c, int sides = 12)
    {
        var axis = Vector3.Normalize(b - a);
        // (u, v, axis) is a right-handed basis, so increasing angle runs counter-clockwise seen from b.
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, reference));
        var v = Vector3.Cross(axis, u);
        Vector3 Radial(int i)
        {
            float t = i * MathF.Tau / sides;
            return u * MathF.Cos(t) + v * MathF.Sin(t);
        }

        Rlgl.Begin((int)DrawMode.Quads);
        for (int i = 0; i < sides; i++)
        {
            Vector3 r0 = Radial(i) * radius, r1 = Radial(i + 1) * radius;
            SetColor(c, Facing(Vector3.Normalize(r0 + r1)));
            V(a + r0); V(a + r1); V(b + r1); V(b + r0);
        }
        Rlgl.End();

        Rlgl.Begin((int)DrawMode.Triangles);
        SetColor(c, Facing(axis));
        for (int i = 0; i < sides; i++) { V(b); V(b + Radial(i) * radius); V(b + Radial(i + 1) * radius); }
        SetColor(c, Facing(-axis));
        for (int i = 0; i < sides; i++) { V(a); V(a + Radial(i + 1) * radius); V(a + Radial(i) * radius); }
        Rlgl.End();
    }

    /// <summary>Brightness for a surface facing <paramref name="n"/>, blending the fixed box-face values.</summary>
    static float Facing(Vector3 n)
    {
        float side = SideZ + (SideX - SideZ) * MathF.Abs(n.X);
        return n.Y >= 0 ? float.Lerp(side, Top, n.Y) : float.Lerp(side, Bottom, -n.Y);
    }

    /// <summary>Flat quad facing up (+Y), used for floor tiles.</summary>
    public static void FloorQuad(float x0, float z0, float x1, float z1, float y, Color c)
    {
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(c.R, c.G, c.B, c.A);
        V(x0, y, z1); V(x1, y, z1); V(x1, y, z0); V(x0, y, z0);
        Rlgl.End();
    }

    static void SetColor(Color c, float shade)
    {
        var s = MathUtil.Shade(c, shade);
        Rlgl.Color4ub(s.R, s.G, s.B, s.A);
    }

    static void V(float x, float y, float z) => Rlgl.Vertex3f(x, y, z);
    static void V(Vector3 p) => Rlgl.Vertex3f(p.X, p.Y, p.Z);
}
