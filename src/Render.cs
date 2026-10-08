using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>
/// Draws boxes and cylinders with "fake lighting": each face gets a fixed brightness,
/// so the top is brightest and the bottom darkest. This is the flat, low-poly look
/// Krunker uses, and it costs nothing (no lights, no shaders).
/// Each shape can carry a <see cref="Surface"/> texture, multiplied by its color.
/// </summary>
public static class Render
{
    const float Top = 1.0f, SideX = 0.86f, SideZ = 0.72f, Bottom = 0.55f;

    public static void ShadedCube(Vector3 center, Vector3 size, Color color, Surface surface = Surface.None) =>
        ShadedBox(center - size / 2, center + size / 2, color, surface);

    /// <summary>
    /// Emits 6 quads. Vertices are listed counter-clockwise as seen from outside,
    /// because the GPU culls (skips) faces whose vertices appear clockwise.
    /// Texture coordinates are the distance along the face (measured from its left
    /// edge and down from its top) divided by how many meters one copy of the
    /// texture covers, so textures keep their real-world size on boxes of any size.
    /// </summary>
    public static void ShadedBox(Vector3 a, Vector3 b, Color c, Surface surface = Surface.None)
    {
        var size = b - a;
        float m = Textures.Meters(surface);
        // Face size in texture repeats. m = 0: stretch the texture once over each face.
        float x = m > 0 ? size.X / m : 1, y = m > 0 ? size.Y / m : 1, z = m > 0 ? size.Z / m : 1;

        Rlgl.SetTexture(Textures.Id(surface));
        Rlgl.Begin((int)DrawMode.Quads);

        SetColor(c, Top); // +Y
        V(a.X, b.Y, b.Z, 0, z); V(b.X, b.Y, b.Z, x, z); V(b.X, b.Y, a.Z, x, 0); V(a.X, b.Y, a.Z, 0, 0);

        SetColor(c, Bottom); // -Y
        V(a.X, a.Y, a.Z, 0, 0); V(b.X, a.Y, a.Z, x, 0); V(b.X, a.Y, b.Z, x, z); V(a.X, a.Y, b.Z, 0, z);

        SetColor(c, SideZ); // +Z
        V(a.X, a.Y, b.Z, 0, y); V(b.X, a.Y, b.Z, x, y); V(b.X, b.Y, b.Z, x, 0); V(a.X, b.Y, b.Z, 0, 0);

        SetColor(c, SideZ); // -Z
        V(b.X, a.Y, a.Z, 0, y); V(a.X, a.Y, a.Z, x, y); V(a.X, b.Y, a.Z, x, 0); V(b.X, b.Y, a.Z, 0, 0);

        SetColor(c, SideX); // +X
        V(b.X, a.Y, b.Z, 0, y); V(b.X, a.Y, a.Z, z, y); V(b.X, b.Y, a.Z, z, 0); V(b.X, b.Y, b.Z, 0, 0);

        SetColor(c, SideX); // -X
        V(a.X, a.Y, a.Z, 0, y); V(a.X, a.Y, b.Z, z, y); V(a.X, b.Y, b.Z, z, 0); V(a.X, b.Y, a.Z, 0, 0);

        Rlgl.End();
    }

    /// <summary>Box rotated around the X axis (positive tips the top forward, toward +Z), e.g. a slanted grip or magazine.</summary>
    public static void TiltedCube(Vector3 center, Vector3 size, float degrees, Color color, Surface surface = Surface.None)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(center.X, center.Y, center.Z);
        Rlgl.Rotatef(degrees, 1, 0, 0);
        ShadedCube(Vector3.Zero, size, color, surface);
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Cylinder from <paramref name="a"/> to <paramref name="b"/> with capped ends. Each side
    /// quad gets the same fake lighting as boxes, picked from the direction it faces.
    /// The texture wraps around the side, so it repeats every <c>Meters</c> around and along.
    /// </summary>
    public static void ShadedCylinder(Vector3 a, Vector3 b, float radius, Color c, Surface surface = Surface.None, int sides = 12)
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

        float m = Textures.Meters(surface);
        float around = m > 0 ? MathF.Tau * radius / m : 1, along = m > 0 ? Vector3.Distance(a, b) / m : 1;
        float cap = m > 0 ? radius / m : 0.5f;

        Rlgl.SetTexture(Textures.Id(surface));
        Rlgl.Begin((int)DrawMode.Quads);
        for (int i = 0; i < sides; i++)
        {
            Vector3 r0 = Radial(i) * radius, r1 = Radial(i + 1) * radius;
            float u0 = around * i / sides, u1 = around * (i + 1) / sides;
            SetColor(c, Facing(Vector3.Normalize(r0 + r1)));
            V(a + r0, u0, along); V(a + r1, u1, along); V(b + r1, u1, 0); V(b + r0, u0, 0);
        }
        Rlgl.End();

        // Caps: the texture is laid flat across each end.
        Vector2 CapUv(int i) => new Vector2(0.5f, 0.5f) + new Vector2(MathF.Cos(i * MathF.Tau / sides), MathF.Sin(i * MathF.Tau / sides)) * cap;
        var mid = new Vector2(0.5f, 0.5f);
        Rlgl.Begin((int)DrawMode.Triangles);
        SetColor(c, Facing(axis));
        for (int i = 0; i < sides; i++) { V(b, mid); V(b + Radial(i) * radius, CapUv(i)); V(b + Radial(i + 1) * radius, CapUv(i + 1)); }
        SetColor(c, Facing(-axis));
        for (int i = 0; i < sides; i++) { V(a, mid); V(a + Radial(i + 1) * radius, CapUv(i + 1)); V(a + Radial(i) * radius, CapUv(i)); }
        Rlgl.End();
    }

    /// <summary>
    /// Switches back to raylib's plain white texture. Call after a group of textured shapes,
    /// before raylib's own Draw* functions (tracers, particles), which don't pick a texture themselves.
    /// </summary>
    public static void ResetTexture() => Rlgl.SetTexture(Rlgl.GetTextureIdDefault());

    /// <summary>Brightness for a surface facing <paramref name="n"/>, blending the fixed box-face values.</summary>
    static float Facing(Vector3 n)
    {
        float side = SideZ + (SideX - SideZ) * MathF.Abs(n.X);
        return n.Y >= 0 ? float.Lerp(side, Top, n.Y) : float.Lerp(side, Bottom, -n.Y);
    }

    static void SetColor(Color c, float shade)
    {
        var s = MathUtil.Shade(c, shade);
        Rlgl.Color4ub(s.R, s.G, s.B, s.A);
    }

    static void V(float x, float y, float z, float u, float v)
    {
        Rlgl.TexCoord2f(u, v);
        Rlgl.Vertex3f(x, y, z);
    }

    static void V(Vector3 p, float u, float v) => V(p.X, p.Y, p.Z, u, v);
    static void V(Vector3 p, Vector2 uv) => V(p.X, p.Y, p.Z, uv.X, uv.Y);
}
