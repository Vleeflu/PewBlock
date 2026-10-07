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
public sealed class Bot(string name, Color color) : ICombatant
{
    const float MoveSpeed = 6.2f, SightRange = 70f, ShotDamage = 10f;
    public const float Height = 1.85f, HeadBottom = 1.35f;

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

        var muzzle = Body.Position + new Vector3(0, 1.18f, 0)
                     + MathUtil.FlatForward(Yaw) * 0.8f + MathUtil.FlatRight(Yaw) * 0.3f;
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

    /// <summary>Blocky character, rotated to face Yaw.</summary>
    public void Draw(float alpha)
    {
        if (!Alive) return;
        var pos = RenderPosition(alpha);
        bool flash = HurtFlash > 0;
        var shirt = flash ? Color.White : Color;
        var pants = MathUtil.Shade(Color, 0.55f);
        var skin = flash ? Color.White : new Color(240, 200, 160, 255);
        var dark = new Color(40, 40, 50, 255);
        float swing = MathF.Sin(walkPhase) * 0.18f;

        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(Yaw * MathUtil.Rad2Deg, 0, 1, 0);
        Render.ShadedCube(new(0.17f, 0.4f, swing), new(0.26f, 0.8f, 0.28f), pants);
        Render.ShadedCube(new(-0.17f, 0.4f, -swing), new(0.26f, 0.8f, 0.28f), pants);
        Render.ShadedCube(new(0, 1.075f, 0), new(0.62f, 0.55f, 0.36f), shirt);
        Render.ShadedCube(new(0, 1.6f, 0), new(0.5f, 0.5f, 0.5f), skin);
        Render.ShadedCube(new(0.11f, 1.65f, 0.255f), new(0.08f, 0.08f, 0.02f), dark);
        Render.ShadedCube(new(-0.11f, 1.65f, 0.255f), new(0.08f, 0.08f, 0.02f), dark);
        Render.ShadedCube(new(-0.3f, 1.1f, 0.25f), new(0.14f, 0.14f, 0.5f), shirt);
        Render.ShadedCube(new(-0.3f, 1.18f, 0.55f), new(0.09f, 0.12f, 0.55f), dark);
        Rlgl.PopMatrix();
    }
}
