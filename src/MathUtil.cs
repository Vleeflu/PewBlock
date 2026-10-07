using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>Small math helpers shared by the whole game.</summary>
public static class MathUtil
{
    public const float Rad2Deg = 180f / MathF.PI;
    public const float Deg2Rad = MathF.PI / 180f;

    /// <summary>Direction you are looking at, from yaw (left/right) and pitch (up/down) in radians.</summary>
    public static Vector3 Forward(float yaw, float pitch) =>
        new(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch));

    /// <summary>Forward direction flattened onto the ground (used for walking).</summary>
    public static Vector3 FlatForward(float yaw) => new(MathF.Sin(yaw), 0, MathF.Cos(yaw));

    /// <summary>Right-hand direction on the ground. Equals Cross(FlatForward, Up).</summary>
    public static Vector3 FlatRight(float yaw) => new(-MathF.Cos(yaw), 0, MathF.Sin(yaw));

    /// <summary>Frame-rate independent smoothing: moves current toward target by a fixed fraction per second.</summary>
    public static float Approach(float current, float target, float rate, float dt) =>
        current + (target - current) * (1f - MathF.Exp(-rate * dt));

    /// <summary>Interpolates between two angles along the shortest path.</summary>
    public static float LerpAngle(float from, float to, float t)
    {
        float delta = MathF.IEEERemainder(to - from, MathF.Tau);
        return from + delta * Math.Clamp(t, 0f, 1f);
    }

    public static float Range(float min, float max) => min + (float)Random.Shared.NextDouble() * (max - min);

    /// <summary>Random point inside a unit sphere (rejection sampling).</summary>
    public static Vector3 RandomInSphere()
    {
        while (true)
        {
            var v = new Vector3(Range(-1, 1), Range(-1, 1), Range(-1, 1));
            if (v.LengthSquared() <= 1f) return v;
        }
    }

    public static float HorizontalLength(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    /// <summary>Darkens (f &lt; 1) or brightens (f &gt; 1) a color.</summary>
    public static Color Shade(Color c, float f) => new(
        (byte)Math.Min(255f, c.R * f),
        (byte)Math.Min(255f, c.G * f),
        (byte)Math.Min(255f, c.B * f),
        c.A);
}
