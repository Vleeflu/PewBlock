using System.Numerics;

namespace PewBlock;

/// <summary>
/// Quake-style movement math. Krunker (like Quake, CS and Source games) uses
/// these exact ideas; they are what make bunny hopping and air strafing work.
/// Only the horizontal (X/Z) part of the velocity is touched here.
/// </summary>
public static class Movement
{
    /// <summary>Slows you down while on the ground.</summary>
    public static void ApplyFriction(ref Vector3 velocity, float friction, float stopSpeed, float dt)
    {
        float speed = MathUtil.HorizontalLength(velocity);
        if (speed < 0.001f) { velocity.X = 0; velocity.Z = 0; return; }

        // Below stopSpeed we brake as if we were going stopSpeed, so you stop crisply.
        float control = MathF.Max(speed, stopSpeed);
        float newSpeed = MathF.Max(speed - control * friction * dt, 0);
        float k = newSpeed / speed;
        velocity.X *= k;
        velocity.Z *= k;
    }

    /// <summary>
    /// Adds speed toward wishDir, but only until your speed *along wishDir*
    /// reaches wishSpeed. Speed in other directions is left alone.
    /// </summary>
    public static void Accelerate(ref Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float current = velocity.X * wishDir.X + velocity.Z * wishDir.Z;
        float add = wishSpeed - current;
        if (add <= 0) return;
        float gain = MathF.Min(accel * wishSpeed * dt, add);
        velocity.X += wishDir.X * gain;
        velocity.Z += wishDir.Z * gain;
    }

    /// <summary>
    /// Same as Accelerate, but the speed limit is tiny (airCap). Because the limit
    /// is measured only along wishDir, pressing A/D while turning the mouse keeps
    /// wishDir almost perpendicular to your velocity, so "current" stays near zero
    /// and you keep gaining speed. That is air strafing.
    /// </summary>
    public static void AirAccelerate(ref Vector3 velocity, Vector3 wishDir, float wishSpeed, float airCap, float accel, float dt)
    {
        float current = velocity.X * wishDir.X + velocity.Z * wishDir.Z;
        float add = MathF.Min(wishSpeed, airCap) - current;
        if (add <= 0) return;
        float gain = MathF.Min(accel * wishSpeed * dt, add);
        velocity.X += wishDir.X * gain;
        velocity.Z += wishDir.Z * gain;
    }
}
