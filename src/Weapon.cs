using Raylib_cs;

namespace PewBlock;

/// <summary>Static stats of a weapon type. Spreads and recoil are in radians.</summary>
public sealed record WeaponDef(
    string Name,
    float Damage,
    float HeadshotMultiplier,
    float FireInterval,
    bool Automatic,
    int MagazineSize,
    float ReloadTime,
    float HipSpread,
    float AdsSpread,
    float MoveSpread,      // extra spread per m/s of movement speed
    float AdsFov,
    float Recoil,
    bool HasScope,
    float MuzzleDistance,  // barrel tip distance along the gun model, where tracers and the muzzle flash start
    GunModel Model,
    Color Color,           // main body color of the model
    string Sound);         // file name in source/, played on every shot

public static class WeaponDefs
{
    public static readonly WeaponDef Rifle = new(
        "M4A4", Damage: 23, HeadshotMultiplier: 1.5f, FireInterval: 0.1f, Automatic: true,
        MagazineSize: 30, ReloadTime: 1.5f, HipSpread: 0.03f, AdsSpread: 0.006f, MoveSpread: 0.0025f,
        AdsFov: 55f, Recoil: 0.018f, HasScope: false, MuzzleDistance: 0.475f, Model: GunModel.M4A4,
        Color: new Color(58, 61, 68, 255), Sound: "burst.mp3");

    public static readonly WeaponDef Sniper = new(
        "AWP", Damage: 100, HeadshotMultiplier: 1.5f, FireInterval: 1.0f, Automatic: false,
        MagazineSize: 3, ReloadTime: 2.0f, HipSpread: 0.12f, AdsSpread: 0f, MoveSpread: 0.0015f,
        AdsFov: 22f, Recoil: 0.09f, HasScope: true, MuzzleDistance: 0.77f, Model: GunModel.Awp,
        Color: new Color(74, 98, 62, 255), Sound: "sniper.mp3");
}

/// <summary>A weapon the player carries: ammo, fire cooldown and reload state.</summary>
public sealed class Weapon(WeaponDef def)
{
    public WeaponDef Def { get; } = def;
    public int Ammo { get; private set; } = def.MagazineSize;
    public float ReloadTimer { get; private set; }
    float cooldown;

    public bool IsReloading => ReloadTimer > 0;
    public float ReloadProgress => IsReloading ? 1 - ReloadTimer / Def.ReloadTime : 1;
    public bool CanFire => cooldown <= 0 && !IsReloading && Ammo > 0;

    public void Update(float dt)
    {
        // Cooldown may dip slightly below 0 (at most one frame) so the leftover time
        // carries into the next shot. That keeps the fire rate exact at any FPS.
        if (cooldown > 0) cooldown -= dt;
        if (IsReloading)
        {
            ReloadTimer -= dt;
            if (ReloadTimer <= 0) { ReloadTimer = 0; Ammo = Def.MagazineSize; }
        }
    }

    public void Fire()
    {
        Ammo--;
        cooldown += Def.FireInterval;
    }

    public void StartReload()
    {
        if (!IsReloading && Ammo < Def.MagazineSize) ReloadTimer = Def.ReloadTime;
    }

    public void CancelReload() => ReloadTimer = 0;

    public void Refill()
    {
        Ammo = Def.MagazineSize;
        ReloadTimer = 0;
        cooldown = 0;
    }
}
