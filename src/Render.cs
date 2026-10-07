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
}
