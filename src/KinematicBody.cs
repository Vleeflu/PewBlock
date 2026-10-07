using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>
/// An upright box that moves through the world without passing through blocks.
/// Used by both the player and the bots. Position is the bottom-center (feet).
///
/// Collision strategy: move one axis at a time (X, then Z, then Y). After each
/// axis, if we overlap a block, push back out along that same axis. Because the
/// map is made of axis-aligned boxes this is simple and very stable.
/// </summary>
public sealed class KinematicBody(float width, float height)
{
    /// <summary>Ledges up to this height are climbed automatically (stairs).</summary>
    public const float StepHeight = World.StepRise + 0.05f;
    const float Skin = 0.001f; // tiny gap so we never rest exactly inside a surface

    public Vector3 Position;
    public Vector3 PrevPosition; // position at the previous tick, for smooth rendering
    public Vector3 Velocity;
    public readonly float HalfWidth = width / 2;
    public float Height = height;
    public bool Grounded { get; private set; }

    public BoundingBox BoundsAt(Vector3 feet, float height) => new(
        new Vector3(feet.X - HalfWidth, feet.Y, feet.Z - HalfWidth),
        new Vector3(feet.X + HalfWidth, feet.Y + height, feet.Z + HalfWidth));

    public BoundingBox BoundsAt(Vector3 feet) => BoundsAt(feet, Height);

    public void Teleport(Vector3 feet)
    {
        Position = PrevPosition = feet;
        Velocity = Vector3.Zero;
        Grounded = false;
    }

    /// <summary>Moves by Velocity * dt. Returns true if a wall blocked horizontal movement.</summary>
    public bool Move(World world, float dt)
    {
        bool canStep = Grounded;
        bool blocked = MoveHorizontal(world, Velocity.X * dt, 0, canStep);
        blocked |= MoveHorizontal(world, 0, Velocity.Z * dt, canStep);
        MoveVertical(world, Velocity.Y * dt);
        return blocked;
    }

    bool MoveHorizontal(World world, float dx, float dz, bool canStep)
    {
        if (dx == 0 && dz == 0) return false;
        var target = Position + new Vector3(dx, 0, dz);
        if (!world.Overlaps(BoundsAt(target))) { Position = target; return false; }

        // Step up: if the same move works from StepHeight higher, climb, then drop onto the step.
        if (canStep)
        {
            var lift = new Vector3(0, StepHeight, 0);
            if (!world.Overlaps(BoundsAt(Position + lift)) && !world.Overlaps(BoundsAt(target + lift)))
            {
                Position = target + lift;
                PrevPosition.Y += StepHeight * 0.5f; // soften the visual pop
                MoveVertical(world, -StepHeight);
                return false;
            }
        }

        // Blocked: move, then push back out of every block we now overlap.
        Position = target;
        foreach (var b in world.Blocks)
        {
            if (!World.Intersects(BoundsAt(Position), b.Box)) continue;
            if (dx > 0) Position.X = b.Box.Min.X - HalfWidth - Skin;
            else if (dx < 0) Position.X = b.Box.Max.X + HalfWidth + Skin;
            if (dz > 0) Position.Z = b.Box.Min.Z - HalfWidth - Skin;
            else if (dz < 0) Position.Z = b.Box.Max.Z + HalfWidth + Skin;
        }
        if (dx != 0) Velocity.X = 0;
        else Velocity.Z = 0;
        return true;
    }

    void MoveVertical(World world, float dy)
    {
        Grounded = false;
        if (dy == 0) return;
        Position.Y += dy;
        foreach (var b in world.Blocks)
        {
            if (!World.Intersects(BoundsAt(Position), b.Box)) continue;
            if (dy < 0)
            {
                Position.Y = b.Box.Max.Y + Skin; // landed on top
                Grounded = true;
            }
            else
            {
                Position.Y = b.Box.Min.Y - Height - Skin; // bonked head
            }
            Velocity.Y = 0;
        }
    }
}
