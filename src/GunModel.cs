using System.Numerics;
using Raylib_cs;

namespace PewBlock;

public enum GunModel { M4A4, Awp }

/// <summary>
/// Gun meshes built from shaded boxes and cylinders, in meters. Used for the
/// first-person view model and for the guns bots carry.
/// Local axes: +Z forward (muzzle), +Y up, -X is the gun's right side.
/// The barrel runs along (0, BarrelY, z); the receiver sits around z = 0.
/// </summary>
public static class GunModels
{
    public const float BarrelY = 0.02f;

    static readonly Color Black = new(30, 31, 34, 255);
    static readonly Color Steel = new(92, 95, 102, 255);
    static readonly Color Lens = new(60, 110, 170, 255);
    static readonly Color FlashCore = new(255, 250, 215, 255);
    static readonly Color FlashOuter = new(255, 185, 70, 255);

    public static void Draw(GunModel model, Color body)
    {
        if (model == GunModel.M4A4) DrawM4A4(body);
        else DrawAwp(body);
    }

    /// <summary>Colt M4A4-style carbine: collapsible stock, flat-top rail, curved magazine, flash hider.</summary>
    static void DrawM4A4(Color body)
    {
        var upper = MathUtil.Shade(body, 1.18f);

        // Collapsible stock on a buffer tube.
        Cyl(new(0, 0.025f, -0.40f), new(0, 0.025f, -0.17f), 0.015f, Steel);
        Cube(new(0, 0.002f, -0.345f), new(0.046f, 0.08f, 0.12f), body);
        Render.TiltedCube(new(0, -0.042f, -0.33f), new(0.04f, 0.03f, 0.1f), -12, body);
        Cube(new(0, -0.005f, -0.412f), new(0.05f, 0.1f, 0.016f), Black); // butt pad

        // Lower receiver, magazine well, curved magazine, pistol grip, trigger.
        Cube(new(0, -0.012f, -0.1f), new(0.05f, 0.05f, 0.18f), body);
        Cube(new(0, -0.045f, -0.035f), new(0.042f, 0.04f, 0.07f), body);
        Render.TiltedCube(new(0, -0.1f, -0.027f), new(0.034f, 0.09f, 0.058f), -8, Black);
        Render.TiltedCube(new(0, -0.17f, -0.015f), new(0.034f, 0.07f, 0.058f), -20, Black);
        Render.TiltedCube(new(0, -0.08f, -0.165f), new(0.034f, 0.1f, 0.042f), 18, Black);
        Cube(new(0, -0.062f, -0.088f), new(0.012f, 0.006f, 0.075f), Black); // trigger guard
        Cube(new(0, -0.046f, -0.09f), new(0.006f, 0.026f, 0.006f), Steel);  // trigger

        // Upper receiver: ejection port and forward assist on the right, charging handle at the back.
        Cube(new(0, 0.032f, -0.09f), new(0.048f, 0.044f, 0.22f), upper);
        Cube(new(-0.0245f, 0.03f, -0.07f), new(0.003f, 0.018f, 0.05f), Black);
        Cyl(new(-0.024f, 0.04f, -0.16f), new(-0.038f, 0.04f, -0.16f), 0.008f, Steel);
        Cube(new(0, 0.05f, -0.205f), new(0.03f, 0.01f, 0.02f), Black);

        // Picatinny rail with low ridges along the whole top.
        Cube(new(0, 0.058f, 0.03f), new(0.026f, 0.012f, 0.42f), Black);
        var ridge = MathUtil.Shade(Black, 1.5f);
        for (int i = 0; i < 20; i++)
            Cube(new(0, 0.0655f, -0.17f + i * 0.021f), new(0.028f, 0.004f, 0.007f), ridge);

        // Flip-up rear sight: two small ears around an aperture. Aiming down sights puts the eye right behind it.
        Cube(new(0, 0.074f, -0.165f), new(0.028f, 0.012f, 0.025f), Black);
        Cube(new(0.009f, 0.091f, -0.165f), new(0.006f, 0.022f, 0.01f), Black);
        Cube(new(-0.009f, 0.091f, -0.165f), new(0.006f, 0.022f, 0.01f), Black);

        // Handguard with vent slots, and the ring that holds it.
        Cyl(new(0, 0.022f, 0.012f), new(0, 0.022f, 0.028f), 0.036f, Steel);
        Cube(new(0, 0.022f, 0.13f), new(0.058f, 0.06f, 0.22f), body);
        foreach (int side in new[] { -1, 1 })
            for (int i = 0; i < 5; i++)
                Cube(new(side * 0.0295f, 0.022f, 0.055f + i * 0.04f), new(0.002f, 0.014f, 0.024f), Black);

        // Barrel, front sight post, flash hider.
        Cyl(new(0, BarrelY, 0.24f), new(0, BarrelY, 0.43f), 0.0105f, Black);
        Cube(new(0, 0.045f, 0.275f), new(0.02f, 0.035f, 0.022f), Black);
        Cube(new(0, 0.075f, 0.275f), new(0.022f, 0.024f, 0.012f), Black);
        Cube(new(0, 0.092f, 0.275f), new(0.006f, 0.026f, 0.006f), Black); // post tip level with the rear ears
        Cyl(new(0, BarrelY, 0.43f), new(0, BarrelY, 0.475f), 0.015f, Black);
        Cube(new(0, BarrelY, 0.46f), new(0.032f, 0.004f, 0.02f), Steel);
        Cube(new(0, BarrelY, 0.46f), new(0.004f, 0.032f, 0.02f), Steel);
    }

    /// <summary>AWP-style sniper: olive thumbhole stock, bolt action, big scope, muzzle brake.</summary>
    static void DrawAwp(Color body)
    {
        var bodyDark = MathUtil.Shade(body, 0.78f);

        // Thumbhole stock: two bars leave a hole, with a raised cheek rest on top.
        Cube(new(0, -0.03f, -0.405f), new(0.05f, 0.12f, 0.15f), body);
        Cube(new(0, 0.04f, -0.39f), new(0.044f, 0.03f, 0.16f), body);
        Cube(new(0, -0.035f, -0.489f), new(0.056f, 0.13f, 0.018f), Black); // butt pad
        Cube(new(0, -0.075f, -0.29f), new(0.044f, 0.04f, 0.08f), body);
        Cube(new(0, 0.01f, -0.29f), new(0.044f, 0.03f, 0.08f), body);
        Render.TiltedCube(new(0, -0.06f, -0.235f), new(0.036f, 0.1f, 0.04f), 14, bodyDark);

        // Chassis, metal action on top, bolt handle with a round knob on the right.
        Cube(new(0, -0.005f, -0.06f), new(0.058f, 0.07f, 0.38f), body);
        Cube(new(0, 0.042f, -0.07f), new(0.04f, 0.03f, 0.24f), Black);
        Cyl(new(-0.02f, 0.045f, -0.15f), new(-0.068f, 0.03f, -0.15f), 0.007f, Steel);
        Cube(new(-0.074f, 0.028f, -0.15f), new(0.024f, 0.024f, 0.024f), Black);

        // Box magazine and trigger.
        Cube(new(0, -0.065f, -0.02f), new(0.04f, 0.06f, 0.09f), Black);
        Cube(new(0, -0.062f, -0.15f), new(0.012f, 0.006f, 0.07f), Black);
        Cube(new(0, -0.05f, -0.152f), new(0.006f, 0.024f, 0.006f), Steel);

        // Forend, tapering toward the barrel.
        Cube(new(0, -0.008f, 0.2f), new(0.054f, 0.06f, 0.14f), body);
        Cube(new(0, -0.008f, 0.295f), new(0.046f, 0.05f, 0.05f), bodyDark);

        // Long barrel and a ported muzzle brake.
        Cyl(new(0, BarrelY, 0.13f), new(0, BarrelY, 0.7f), 0.012f, Black);
        Cyl(new(0, BarrelY, 0.69f), new(0, BarrelY, 0.77f), 0.019f, Black);
        Cube(new(0, BarrelY, 0.715f), new(0.042f, 0.012f, 0.012f), Steel);
        Cube(new(0, BarrelY, 0.745f), new(0.042f, 0.012f, 0.012f), Steel);

        // Scope: two rings, tube, eyepiece, objective bell with a glass lens, turrets.
        const float sy = 0.098f;
        Cube(new(0, 0.065f, -0.13f), new(0.03f, 0.025f, 0.022f), Black);
        Cube(new(0, 0.065f, 0f), new(0.03f, 0.025f, 0.022f), Black);
        Cyl(new(0, sy, -0.2f), new(0, sy, 0.1f), 0.02f, Black);
        Cyl(new(0, sy, -0.27f), new(0, sy, -0.19f), 0.027f, Black);
        Cyl(new(0, sy, 0.08f), new(0, sy, 0.11f), 0.026f, Black);
        Cyl(new(0, sy, 0.11f), new(0, sy, 0.2f), 0.032f, Black);
        Cyl(new(0, sy, 0.2f), new(0, sy, 0.203f), 0.027f, Lens);
        Cyl(new(0, sy + 0.018f, -0.06f), new(0, sy + 0.04f, -0.06f), 0.012f, Steel);
        Cyl(new(-0.018f, sy, -0.06f), new(-0.04f, sy, -0.06f), 0.012f, Steel);
    }

    /// <summary>Star-shaped flash at the barrel tip, spun around the barrel so each shot looks different.</summary>
    public static void DrawMuzzleFlash(float muzzleZ, float spinDegrees, float size)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(0, BarrelY, muzzleZ);
        Rlgl.Rotatef(spinDegrees, 0, 0, 1);
        Rlgl.Scalef(size, size, size);
        Render.ShadedCylinder(Vector3.Zero, new(0, 0, 0.13f), 0.022f, FlashOuter, 6);
        Render.ShadedCube(new(0, 0, 0.045f), new(0.12f, 0.014f, 0.03f), FlashOuter);
        Render.ShadedCube(new(0, 0, 0.045f), new(0.014f, 0.12f, 0.03f), FlashOuter);
        Render.ShadedCube(new(0, 0, 0.035f), new(0.04f, 0.04f, 0.06f), FlashCore);
        Rlgl.PopMatrix();
    }

    static void Cube(Vector3 center, Vector3 size, Color color) => Render.ShadedCube(center, size, color);
    static void Cyl(Vector3 a, Vector3 b, float radius, Color color) => Render.ShadedCylinder(a, b, radius, color, 10);
}
