using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>A shot fired by a bot this tick.</summary>
public readonly record struct BotShot(ICombatant Target, Vector3 From, Vector3 To, bool Hit, float Damage);

/// <summary>
/// Free-for-all enemy with a tiny state machine:
///   HAS TARGET   → turn toward them, strafe, wait a human-like reaction time, shoot.
///   LOST TARGET  → walk to where they were last seen.
///   OTHERWISE    → wander between random points (inside the next zone in battle royale).
/// Anyone can be a target: the player or another bot. A bot prefers the nearest
/// visible enemy, sticks with its current one, and turns on whoever shoots it.
/// Bots don't aim real bullets; each shot rolls a hit chance that drops with
/// distance and with how fast the target moves.
/// </summary>
public sealed class Bot(string name, Color color, int look) : ICombatant
{
    const float MoveSpeed = 6.2f, SightRange = 70f, ShotDamage = 10f;
    public const float Height = 1.85f, HeadBottom = 1.35f;
    public static readonly WeaponDef Weapon = WeaponDefs.Rifle;
    /// <summary>Where the gun model sits in the bot's local space (-X is right). Drawn larger than life so it reads at a distance.</summary>
    static readonly Vector3 GunMount = new(-0.14f, 1.15f, 0.3f);
    const float GunScale = 1.4f;

    public string Name { get; } = name;
    public Color Color { get; } = color;
    public readonly KinematicBody Body = new(0.7f, Height);
    public float Health { get; private set; }
    public bool Alive { get; private set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public float RespawnTimer { get; set; }
    public int Placement { get; set; }
    public float Yaw, HurtFlash, HealthBarTimer;

    public Vector3 Feet => Body.Position;
    public Vector3 Eye => Body.Position + new Vector3(0, 1.6f, 0);
    public float HorizontalSpeed => MathUtil.HorizontalLength(Body.Velocity);
    public bool Grounded => Body.Grounded;

    float reactionTime, reaction, shootCooldown, strafeDir = 1, strafeTimer;
    float stuckTimer, scanTimer, lastKnownTimer, walkPhase, timeSinceDamage;
    bool jumpQueued;
    Vector3 waypoint, lastKnown;
    ICombatant? target, attacker;
    readonly List<(ICombatant Who, float Score)> candidates = [];

    public BoundingBox HeadBox => new(
        Body.Position + new Vector3(-0.25f, HeadBottom, -0.25f),
        Body.Position + new Vector3(0.25f, Height, 0.25f));

    public BoundingBox BodyBox => new(
        Body.Position + new Vector3(-0.35f, 0, -0.35f),
        Body.Position + new Vector3(0.35f, HeadBottom, 0.35f));

    public Vector3 RenderPosition(float alpha) => Vector3.Lerp(Body.PrevPosition, Body.Position, alpha);

    public void Spawn(Vector3 feet, World world)
    {
        Body.Teleport(feet);
        Health = Combat.MaxHealth;
        Alive = true;
        Placement = 0;
        Yaw = MathF.Atan2(-feet.X, -feet.Z);
        reactionTime = MathUtil.Range(0.35f, 0.65f);
        reaction = 0;
        shootCooldown = 0.5f;
        target = attacker = null;
        lastKnownTimer = 0;
        waypoint = world.RandomGroundPoint();
    }

    public bool TakeDamage(float amount)
    {
        if (!Alive) return false;
        Health -= amount;
        timeSinceDamage = 0;
        if (Health > 0) return false;
        Health = 0;
        Alive = false;
        return true;
    }

    /// <summary>Called when someone shoots this bot: flash, show health bar, and fight back.</summary>
    public void NotifyAttacked(ICombatant by)
    {
        HurtFlash = 0.1f;
        HealthBarTimer = 3f;
        attacker = by;
        if (target == null)
        {
            lastKnown = by.Feet; // turn around and go find them
            lastKnownTimer = 5f;
        }
        scanTimer = 0; // re-pick a target next tick
    }

    /// <summary>One AI + physics step. <paramref name="zone"/> is null outside battle royale.</summary>
    public BotShot? Tick(World world, IReadOnlyList<ICombatant> everyone, Zone? zone, float dt)
    {
        Body.PrevPosition = Body.Position;
        if (!Alive) return null;
        HurtFlash = MathF.Max(0, HurtFlash - dt);
        HealthBarTimer = MathF.Max(0, HealthBarTimer - dt);
        timeSinceDamage += dt;
        if (timeSinceDamage > Combat.RegenDelay) Health = MathF.Min(Combat.MaxHealth, Health + Combat.RegenRate * dt);

        if (target is { Alive: false }) target = null;
        if (attacker is { Alive: false }) attacker = null;

        // Line-of-sight checks are the most expensive thing a bot does, so only 10x per second.
        if ((scanTimer -= dt) <= 0)
        {
            scanTimer = 0.1f;
            var seen = FindTarget(world, everyone);
            if (seen != target) reaction = 0; // new target: react again
            target = seen;
        }

        Vector3 wish = Vector3.Zero;
        BotShot? shot = null;
        shootCooldown -= dt;

        if (target != null)
        {
            var toTarget = target.Eye - Eye;
            float dist = toTarget.Length();
            lastKnown = target.Feet;
            lastKnownTimer = 5f;
            reaction += dt;
            Yaw = MathUtil.LerpAngle(Yaw, MathF.Atan2(toTarget.X, toTarget.Z), 12f * dt);

            if ((strafeTimer -= dt) <= 0)
            {
                strafeDir = Random.Shared.Next(2) == 0 ? -1 : 1;
                strafeTimer = MathUtil.Range(0.4f, 1.2f);
            }
            var fwd = MathUtil.FlatForward(Yaw);
            wish = MathUtil.FlatRight(Yaw) * strafeDir;
            if (dist > 20) wish += fwd;
            else if (dist < 7) wish -= fwd * 0.7f;
            if (Random.Shared.NextDouble() < 0.7 * dt) jumpQueued = true;

            if (reaction >= reactionTime && shootCooldown <= 0)
                shot = Shoot(world, target, dist);
        }
        else
        {
            bool chasing = (lastKnownTimer -= dt) > 0;
            if (!chasing && zone != null && !zone.NextContains(waypoint)) waypoint = PickWaypoint(world, zone);
            var goal = chasing ? lastKnown : waypoint;
            var to = goal - Body.Position;
            to.Y = 0;
            if (to.Length() < 1.5f)
            {
                if (chasing) lastKnownTimer = 0;
                else waypoint = PickWaypoint(world, zone);
            }
            else
            {
                wish = Vector3.Normalize(to);
            }
        }

        // Battle royale: outside the zone, running back in matters more than anything else.
        if (zone != null && !zone.Contains(Body.Position))
        {
            var toZone = new Vector3(zone.Center.X - Body.Position.X, 0, zone.Center.Y - Body.Position.Z);
            if (toZone.LengthSquared() > 0.01f) wish = wish * 0.4f + Vector3.Normalize(toZone);
        }
        if (wish.LengthSquared() > 1) wish = Vector3.Normalize(wish);
        if (target == null && wish != Vector3.Zero)
            Yaw = MathUtil.LerpAngle(Yaw, MathF.Atan2(wish.X, wish.Z), 6f * dt);

        // Same movement rules as the player, just simpler.
        if (Body.Grounded)
        {
            if (jumpQueued)
            {
                Body.Velocity.Y = Player.JumpSpeed;
                jumpQueued = false;
            }
            else
            {
                Movement.ApplyFriction(ref Body.Velocity, 6f, 2.5f, dt);
            }
            Movement.Accelerate(ref Body.Velocity, wish, MoveSpeed, 10f, dt);
        }
        else
        {
            Movement.AirAccelerate(ref Body.Velocity, wish, MoveSpeed, 0.8f, 12f, dt);
        }
        Body.Velocity.Y -= Player.Gravity * dt;
        bool blocked = Body.Move(world, dt);

        // Ran into something: try jumping over it; if still stuck, pick somewhere else.
        float speed = HorizontalSpeed;
        if (blocked && Body.Grounded) jumpQueued = true;
        stuckTimer = wish != Vector3.Zero && speed < 1f ? stuckTimer + dt : 0;
        if (stuckTimer > 1f)
        {
            waypoint = PickWaypoint(world, zone);
            lastKnownTimer = 0;
            strafeDir = -strafeDir;
            stuckTimer = 0;
        }
        walkPhase += speed * dt * 1.8f;
        return shot;
    }

    static Vector3 PickWaypoint(World world, Zone? zone) =>
        zone == null ? world.RandomGroundPoint() : world.RandomGroundPoint(zone.NextCenter, zone.NextRadius);

    /// <summary>
    /// Nearest visible living enemy. Distances are scaled down for the current
    /// target (stick with it) and for whoever last shot us (revenge), so those win ties.
    /// </summary>
    ICombatant? FindTarget(World world, IReadOnlyList<ICombatant> everyone)
    {
        var eye = Eye;
        candidates.Clear();
        foreach (var c in everyone)
        {
            if (ReferenceEquals(c, this) || !c.Alive) continue;
            float score = Vector3.Distance(eye, c.Eye);
            if (score > SightRange) continue;
            if (ReferenceEquals(c, target)) score *= 0.6f;
            if (ReferenceEquals(c, attacker)) score *= 0.5f;
            candidates.Add((c, score));
        }
        candidates.Sort(static (a, b) => a.Score.CompareTo(b.Score));
        foreach (var (who, _) in candidates)
            if (world.LineOfSight(eye, who.Eye)) return who;
        return null;
    }

    BotShot Shoot(World world, ICombatant victim, float dist)
    {
        shootCooldown = MathUtil.Range(0.3f, 0.55f);
        float chance = 0.8f - dist * 0.012f - victim.HorizontalSpeed * 0.035f;
        if (!victim.Grounded) chance -= 0.12f;
        chance = Math.Clamp(chance, 0.08f, 0.8f);
        bool hit = Random.Shared.NextDouble() < chance;

        var muzzle = Body.Position + new Vector3(0, GunMount.Y + GunScale * GunModels.BarrelY, 0)
                     + MathUtil.FlatForward(Yaw) * (GunMount.Z + GunScale * Weapon.MuzzleDistance) + MathUtil.FlatRight(Yaw) * -GunMount.X;
        var aim = victim.Eye - new Vector3(0, 0.4f, 0); // chest
        Vector3 end;
        if (hit)
        {
            end = aim;
        }
        else
        {
            var dir = Vector3.Normalize(aim + MathUtil.RandomInSphere() * 1.5f - muzzle);
            end = world.Raycast(new Ray(muzzle, dir), 120f, out var h) ? h.Point : muzzle + dir * 120f;
        }
        return new BotShot(victim, muzzle, end, hit, ShotDamage);
    }

    // Looks come from the bot's roster index, so each bot always looks the same and neighbors differ.
    static readonly Color[] SkinTones =
    [
        new(240, 200, 160, 255), new(205, 150, 110, 255), new(150, 100, 70, 255), new(255, 218, 185, 255),
    ];

    static readonly Color Boot = new(52, 40, 32, 255);
    static readonly Color Strap = new(36, 36, 40, 255);
    static readonly Color Backpack = new(84, 92, 62, 255);
    static readonly Color Glove = new(50, 48, 44, 255);

    /// <summary>
    /// Soldier made of shaded, textured boxes, rotated to face Yaw: leather boots, swinging
    /// camo legs, a camo vest in the bot's color, backpack, both arms on an M4A4, a face, and
    /// one of four headgears. The head fills the head hitbox (1.35 m to 1.85 m) and
    /// everything below it the body hitbox.
    /// </summary>
    public void Draw(float alpha)
    {
        if (!Alive) return;
        var pos = RenderPosition(alpha);
        bool flash = HurtFlash > 0;
        Color C(Color c) => flash ? Color.White : c;
        var vest = C(Color);
        var shirt = C(MathUtil.Shade(Color, 0.7f));
        var pants = C(MathUtil.Shade(Color, 0.42f));
        var skin = C(SkinTones[(look * 3 + 1) % SkinTones.Length]);
        var dark = C(Strap);

        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(Yaw * MathUtil.Rad2Deg, 0, 1, 0);

        // Legs swing from the hip while walking; in the air one is forward, one back.
        float swing = Body.Grounded ? MathF.Sin(walkPhase) * 28f : 22f;
        DrawLeg(0.14f, swing, pants, C(Boot), dark);
        DrawLeg(-0.14f, Body.Grounded ? -swing : -12f, pants, C(Boot), dark);

        // Torso: shirt, vest with pouches, belt, backpack.
        Render.ShadedCube(new(0, 1.15f, 0), new(0.56f, 0.42f, 0.3f), shirt, Surface.Fabric);
        Render.ShadedCube(new(0, 1.17f, 0.005f), new(0.6f, 0.34f, 0.34f), vest, Surface.Fabric);
        for (int i = -1; i <= 1; i++)
            Render.ShadedCube(new(i * 0.16f, 1.08f, 0.19f), new(0.12f, 0.12f, 0.05f), C(MathUtil.Shade(Color, 0.8f)), Surface.Fabric);
        Render.ShadedCube(new(0, 0.95f, 0), new(0.58f, 0.07f, 0.32f), dark, Surface.Leather);
        Render.ShadedCube(new(0, 0.95f, 0.165f), new(0.08f, 0.05f, 0.01f), C(new Color(170, 150, 90, 255)));
        Render.ShadedCube(new(0, 1.15f, -0.24f), new(0.42f, 0.4f, 0.16f), C(Backpack), Surface.Fabric);
        Render.ShadedCube(new(0, 1.3f, -0.25f), new(0.44f, 0.1f, 0.17f), C(MathUtil.Shade(Backpack, 0.8f)), Surface.Fabric);

        // Gun, then both arms reaching for its grip and handguard.
        Rlgl.PushMatrix();
        Rlgl.Translatef(GunMount.X, GunMount.Y, GunMount.Z);
        Rlgl.Scalef(GunScale, GunScale, GunScale);
        if (flash) Render.ShadedCube(Vector3.Zero, new(0.06f, 0.1f, 0.5f), Color.White);
        else GunModels.Draw(Weapon.Model, Weapon.Color);
        Rlgl.PopMatrix();
        var rightHand = GunMount + new Vector3(0, -0.08f, -0.165f) * GunScale;
        var leftHand = GunMount + new Vector3(0.01f, -0.02f, 0.14f) * GunScale;
        DrawArm(new(-0.34f, 1.31f, 0), new(-0.33f, 1.06f, 0.0f), rightHand, shirt, C(Glove));
        DrawArm(new(0.34f, 1.31f, 0), new(0.24f, 1.08f, 0.22f), leftHand, shirt, C(Glove));

        // Neck, head and face.
        Render.ShadedCube(new(0, 1.37f, 0), new(0.18f, 0.06f, 0.18f), skin, Surface.Skin);
        Render.ShadedCube(new(0, 1.58f, 0), new(0.42f, 0.42f, 0.42f), skin, Surface.Skin);
        foreach (int side in new[] { -1, 1 })
        {
            Render.ShadedCube(new(side * 0.1f, 1.61f, 0.212f), new(0.1f, 0.065f, 0.01f), C(Color.White));
            Render.ShadedCube(new(side * 0.09f, 1.605f, 0.219f), new(0.045f, 0.05f, 0.01f), C(new Color(30, 30, 40, 255)));
            Render.ShadedCube(new(side * 0.1f, 1.665f, 0.214f), new(0.11f, 0.022f, 0.012f), C(MathUtil.Shade(skin, 0.45f)));
        }
        Render.ShadedCube(new(0, 1.47f, 0.212f), new(0.12f, 0.024f, 0.01f), C(MathUtil.Shade(skin, 0.55f)));
        DrawHeadgear(look % 4, vest, dark, flash);

        Rlgl.PopMatrix();
        Render.ResetTexture();
    }

    /// <summary>Pants, knee pad and boot, rotated around the hip joint.</summary>
    static void DrawLeg(float x, float degrees, Color pants, Color boot, Color pad)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(x, 0.92f, 0);
        Rlgl.Rotatef(degrees, 1, 0, 0);
        Render.ShadedCube(new(0, -0.42f, 0), new(0.22f, 0.8f, 0.24f), pants, Surface.Fabric);
        Render.ShadedCube(new(0, -0.47f, 0.125f), new(0.2f, 0.13f, 0.03f), pad, Surface.Leather);
        Render.ShadedCube(new(0, -0.85f, 0.035f), new(0.23f, 0.15f, 0.32f), boot, Surface.Leather);
        Rlgl.PopMatrix();
    }

    static void DrawArm(Vector3 shoulder, Vector3 elbow, Vector3 hand, Color sleeve, Color glove)
    {
        Render.ShadedCylinder(shoulder, elbow, 0.075f, sleeve, Surface.Fabric, sides: 8);
        Render.ShadedCylinder(elbow, hand, 0.062f, sleeve, Surface.Fabric, sides: 8);
        Render.ShadedCube(hand, new(0.1f, 0.1f, 0.1f), glove, Surface.Leather);
    }

    /// <summary>0 helmet with goggles, 1 cap, 2 spiky hair, 3 beanie. Stays inside the head hitbox.</summary>
    static void DrawHeadgear(int kind, Color color, Color dark, bool flash)
    {
        switch (kind)
        {
            case 0:
                Render.ShadedCube(new(0, 1.8f, 0), new(0.48f, 0.12f, 0.48f), MathUtil.Shade(color, 0.75f), Surface.PaintedMetal);
                Render.ShadedCube(new(0, 1.75f, 0), new(0.5f, 0.04f, 0.5f), MathUtil.Shade(color, 0.6f), Surface.PaintedMetal);
                Render.ShadedCube(new(0, 1.72f, 0.215f), new(0.36f, 0.07f, 0.03f), dark, Surface.Leather);
                Render.ShadedCube(new(0, 1.72f, 0.232f), new(0.3f, 0.05f, 0.01f), flash ? Color.White : new Color(120, 190, 230, 255));
                break;
            case 1:
                Render.ShadedCube(new(0, 1.81f, -0.01f), new(0.45f, 0.1f, 0.45f), color, Surface.Fabric);
                Render.ShadedCube(new(0, 1.77f, 0.3f), new(0.38f, 0.03f, 0.2f), MathUtil.Shade(color, 0.7f), Surface.Fabric);
                break;
            case 2:
                var hair = flash ? Color.White : new Color(60, 42, 30, 255);
                Render.ShadedCube(new(0, 1.81f, 0), new(0.44f, 0.06f, 0.44f), hair, Surface.Hair);
                Render.ShadedCube(new(0, 1.66f, -0.215f), new(0.44f, 0.26f, 0.04f), hair, Surface.Hair);
                for (int i = -1; i <= 1; i++)
                    Render.ShadedCube(new(i * 0.13f, 1.835f, 0.05f * i), new(0.1f, 0.03f, 0.12f), hair, Surface.Hair);
                break;
            default:
                Render.ShadedCube(new(0, 1.8f, 0), new(0.45f, 0.14f, 0.45f), MathUtil.Shade(color, 0.9f), Surface.Fabric);
                Render.ShadedCube(new(0, 1.74f, 0), new(0.47f, 0.05f, 0.47f), MathUtil.Shade(color, 0.6f), Surface.Fabric);
                break;
        }
    }
}
