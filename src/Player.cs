using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>Keyboard state sampled once per frame and fed to the physics ticks.</summary>
public struct MoveInput
{
    public float Forward, Right;   // -1..1
    public bool JumpHeld, CrouchHeld, CrouchPressed;
}

/// <summary>The local player: movement physics, health, weapons and the first-person gun model.</summary>
public sealed class Player : ICombatant
{
    // Units are meters and seconds.
    public const float StandHeight = 1.8f, CrouchHeight = 1.2f;
    public const float Gravity = 24f, JumpSpeed = 8.2f;
    const float GroundSpeed = 7.5f, CrouchSpeed = 3.5f;
    const float GroundAccel = 10f, Friction = 6f, StopSpeed = 2.5f;
    const float AirCap = 0.8f, AirAccel = 12f;
    const float SlideMinSpeed = 4.5f, SlideBoost = 3.5f, SlideMaxSpeed = 14f, SlideFriction = 0.5f, SlideTime = 0.75f;
    const float MaxHorizontalSpeed = 22f;
    const float MaxPitch = 1.55f;
    const float AdsTime = 0.15f, SwapTime = 0.35f;

    public readonly KinematicBody Body = new(0.6f, StandHeight);
    public float Yaw, Pitch, RecoilPitch;
    public string Name => "You";
    public float Health { get; private set; }
    public bool Alive { get; private set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public float RespawnTimer { get; set; }
    public int Placement { get; set; }
    public bool Crouching, Sliding;
    public float EyeHeight = StandHeight - 0.15f;
    public float AdsT;           // 0 = hip fire, 1 = fully aimed
    public bool WantsAds;
    public readonly Weapon[] Weapons = [new(WeaponDefs.Rifle), new(WeaponDefs.Sniper)];
    public int WeaponIndex;

    float slideTimer, timeSinceDamage, swapTimer, viewKick, bobPhase;
    bool slideQueued;

    public Weapon Weapon => Weapons[WeaponIndex];
    public float HorizontalSpeed => MathUtil.HorizontalLength(Body.Velocity);
    public bool Grounded => Body.Grounded;
    public Vector3 Feet => Body.Position;
    public Vector3 Eye => Body.Position + new Vector3(0, EyeHeight, 0);
    public float ViewPitch => Math.Clamp(Pitch + RecoilPitch, -MaxPitch, MaxPitch);
    public Vector3 AimForward => MathUtil.Forward(Yaw, ViewPitch);
    public bool CanShoot => Alive && swapTimer <= 0 && Weapon.CanFire;

    /// <summary>Eye position, interpolated between the last two physics ticks.</summary>
    public Vector3 EyePosition(float alpha) =>
        Vector3.Lerp(Body.PrevPosition, Body.Position, alpha) + new Vector3(0, EyeHeight, 0);

    public void Spawn(Vector3 feet)
    {
        Body.Teleport(feet);
        Body.Height = StandHeight;
        EyeHeight = StandHeight - 0.15f;
        Yaw = MathF.Atan2(-feet.X, -feet.Z); // face the middle of the map
        Pitch = RecoilPitch = 0;
        Health = Combat.MaxHealth;
        Alive = true;
        Crouching = Sliding = slideQueued = false;
        AdsT = 0;
        foreach (var w in Weapons) w.Refill();
    }

    public void Look(Vector2 mouseDelta, float sensitivity)
    {
        Yaw -= mouseDelta.X * sensitivity;
        Pitch = Math.Clamp(Pitch - mouseDelta.Y * sensitivity, -MaxPitch, MaxPitch);
    }

    public void SwitchWeapon(int index)
    {
        if (index == WeaponIndex || index < 0 || index >= Weapons.Length) return;
        Weapon.CancelReload();
        WeaponIndex = index;
        swapTimer = SwapTime;
    }

    /// <summary>One fixed physics step.</summary>
    public void Tick(World world, MoveInput input, float dt)
    {
        Body.PrevPosition = Body.Position;
        if (!Alive) return;

        var wish = MathUtil.FlatForward(Yaw) * input.Forward + MathUtil.FlatRight(Yaw) * input.Right;
        if (wish.LengthSquared() > 0) wish = Vector3.Normalize(wish);

        bool grounded = Body.Grounded;
        float speed = HorizontalSpeed;

        // Slide: press crouch while moving fast on the ground (or press it in the air
        // and the slide starts when you land).
        if (input.CrouchPressed) slideQueued = true;
        if (!input.CrouchHeld) slideQueued = Sliding = false;
        if (grounded && slideQueued)
        {
            slideQueued = false;
            if (speed >= SlideMinSpeed)
            {
                Sliding = true;
                slideTimer = SlideTime;
                float boosted = MathF.Min(speed + SlideBoost, SlideMaxSpeed);
                Body.Velocity.X *= boosted / speed;
                Body.Velocity.Z *= boosted / speed;
            }
        }
        if (Sliding && (slideTimer -= dt) <= 0) Sliding = false;
        SetCrouched(world, input.CrouchHeld || Sliding, grounded);

        float maxSpeed = (Crouching ? CrouchSpeed : GroundSpeed) * (1f - 0.3f * AdsT);

        // Jump. Holding space jumps again the instant you land, skipping ground
        // friction entirely: that is bunny hopping. The jump tick is the only tick
        // spent on the ground, so it tops your speed along wishDir up to run speed
        // (never beyond, so it can't be abused) - otherwise W+Space would crawl.
        if (grounded && input.JumpHeld)
        {
            Movement.Accelerate(ref Body.Velocity, wish, maxSpeed, float.MaxValue, dt);
            Body.Velocity.Y = JumpSpeed;
            grounded = false;
            Sliding = false;
        }
        if (grounded)
        {
            if (Sliding)
            {
                Movement.ApplyFriction(ref Body.Velocity, SlideFriction, StopSpeed, dt);
                Movement.Accelerate(ref Body.Velocity, wish, 1.5f, 4f, dt); // slight steering
            }
            else
            {
                Movement.ApplyFriction(ref Body.Velocity, Friction, StopSpeed, dt);
                Movement.Accelerate(ref Body.Velocity, wish, maxSpeed, GroundAccel, dt);
            }
        }
        else
        {
            Movement.AirAccelerate(ref Body.Velocity, wish, maxSpeed, AirCap, AirAccel, dt);
        }

        float hs = HorizontalSpeed;
        if (hs > MaxHorizontalSpeed)
        {
            Body.Velocity.X *= MaxHorizontalSpeed / hs;
            Body.Velocity.Z *= MaxHorizontalSpeed / hs;
        }

        Body.Velocity.Y -= Gravity * dt;
        Body.Move(world, dt);

        timeSinceDamage += dt;
        if (timeSinceDamage > Combat.RegenDelay) Health = MathF.Min(Combat.MaxHealth, Health + Combat.RegenRate * dt);
    }

    /// <summary>
    /// On the ground, crouching lowers your head. In the air it pulls your feet up
    /// instead (crouch-jump), letting you reach ledges 0.6 m higher.
    /// </summary>
    void SetCrouched(World world, bool want, bool grounded)
    {
        const float diff = StandHeight - CrouchHeight;
        if (want && !Crouching)
        {
            Crouching = true;
            Body.Height = CrouchHeight;
            if (!grounded)
            {
                Body.Position.Y += diff;
                Body.PrevPosition.Y += diff;
                EyeHeight -= diff; // keep the camera where it was
            }
        }
        else if (!want && Crouching)
        {
            var feet = grounded ? Body.Position : Body.Position - new Vector3(0, diff, 0);
            if (world.Overlaps(Body.BoundsAt(feet, StandHeight))) return; // no room to stand up
            Crouching = false;
            Body.Height = StandHeight;
            if (!grounded)
            {
                Body.Position = feet;
                Body.PrevPosition.Y -= diff;
                EyeHeight += diff;
            }
        }
    }

    /// <summary>Per-frame updates that don't affect physics: weapon timers, ADS, camera smoothing.</summary>
    public void UpdateFrame(float dt)
    {
        Weapon.Update(dt);
        if (swapTimer > 0) swapTimer -= dt;

        bool ads = Alive && WantsAds && !Weapon.IsReloading && swapTimer <= 0;
        AdsT = Math.Clamp(AdsT + (ads ? dt : -dt) / AdsTime, 0f, 1f);

        RecoilPitch = MathUtil.Approach(RecoilPitch, 0, 6f, dt);
        viewKick = MathUtil.Approach(viewKick, 0, 18f, dt);
        EyeHeight = MathUtil.Approach(EyeHeight, Body.Height - 0.15f, 14f, dt);
        if (Body.Grounded && !Sliding) bobPhase += HorizontalSpeed * dt * 1.6f;
    }

    /// <summary>Returns true if this damage killed the player.</summary>
    public bool TakeDamage(float amount)
    {
        if (!Alive) return false;
        Health -= amount;
        timeSinceDamage = 0;
        if (Health > 0) return false;
        Health = 0;
        Alive = false;
        Sliding = false;
        return true;
    }

    /// <summary>Bullet spread cone half-angle in radians. Moving or jumping makes you less accurate.</summary>
    public float CurrentSpread()
    {
        var d = Weapon.Def;
        float spread = d.HipSpread + (d.AdsSpread - d.HipSpread) * AdsT;
        spread += HorizontalSpeed * d.MoveSpread;
        if (!Body.Grounded) spread += 0.04f * (1f - 0.5f * AdsT);
        return spread;
    }

    public void ApplyRecoil()
    {
        float r = Weapon.Def.Recoil * (1f - 0.4f * AdsT);
        RecoilPitch += r;
        Yaw += MathUtil.Range(-1, 1) * r * 0.25f;
        viewKick = 0.06f;
    }

    /// <summary>Gun position relative to the eye. Local axes: +Z forward, +Y up, -X right.</summary>
    Vector3 ViewModelOffset()
    {
        float ads = AdsT * AdsT * (3 - 2 * AdsT); // smoothstep
        float bob = Math.Min(HorizontalSpeed / 7.5f, 1f) * (1f - ads);
        float drop = Math.Max(swapTimer, 0) / SwapTime * 0.3f;
        return new Vector3(
            float.Lerp(-0.2f, 0f, ads) + MathF.Cos(bobPhase) * 0.012f * bob,
            float.Lerp(-0.2f, -0.12f, ads) + MathF.Abs(MathF.Sin(bobPhase)) * 0.015f * bob - drop,
            0.42f - viewKick);
    }

    /// <summary>World position of the gun barrel tip, where tracers start.</summary>
    public Vector3 MuzzlePosition(Vector3 eye)
    {
        var forward = AimForward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var local = ViewModelOffset() + new Vector3(0, 0.02f, Weapon.Def.MuzzleDistance);
        return eye - right * local.X + up * local.Y + forward * local.Z;
    }

    /// <summary>Draws the gun in front of the camera. Depth testing is off so it never clips into walls.</summary>
    public void DrawViewModel(Vector3 eye)
    {
        if (!Alive || (Weapon.Def.HasScope && AdsT > 0.9f)) return;

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableDepthTest();
        Rlgl.PushMatrix();
        Rlgl.Translatef(eye.X, eye.Y, eye.Z);
        Rlgl.Rotatef(Yaw * MathUtil.Rad2Deg, 0, 1, 0);
        Rlgl.Rotatef(-ViewPitch * MathUtil.Rad2Deg, 1, 0, 0);
        var o = ViewModelOffset();
        Rlgl.Translatef(o.X, o.Y, o.Z);

        var c = Weapon.Def.Color;
        var dark = MathUtil.Shade(c, 0.55f);
        var skin = new Color(240, 200, 160, 255);
        if (Weapon.Def.HasScope)
        {
            Render.ShadedCube(new(-0.02f, -0.09f, -0.2f), new(0.1f, 0.1f, 0.25f), skin);
            Render.ShadedCube(new(0, 0, 0.05f), new(0.08f, 0.1f, 0.6f), c);
            Render.ShadedCube(new(0, 0.02f, 0.5f), new(0.03f, 0.03f, 0.45f), dark);
            Render.ShadedCube(new(0, 0.09f, 0.05f), new(0.06f, 0.06f, 0.3f), dark);
        }
        else
        {
            Render.ShadedCube(new(-0.02f, -0.09f, -0.15f), new(0.1f, 0.1f, 0.25f), skin);
            Render.ShadedCube(new(0, 0, 0), new(0.08f, 0.12f, 0.45f), c);
            Render.ShadedCube(new(0, -0.11f, 0.05f), new(0.06f, 0.14f, 0.08f), dark);
            Render.ShadedCube(new(0, 0.02f, 0.33f), new(0.035f, 0.035f, 0.25f), dark);
            Render.ShadedCube(new(0, 0.075f, 0.05f), new(0.03f, 0.03f, 0.1f), dark);
        }

        Rlgl.PopMatrix();
        Rlgl.DrawRenderBatchActive();
        Rlgl.EnableDepthTest();
    }
}
