using System.Numerics;
using Raylib_cs;

namespace PewBlock;

public enum GameState { Menu, Playing, Paused, MatchOver }

/// <summary>Order matches the menu cards and the 1 / 2 / 3 keys.</summary>
public enum GameMode { Deathmatch, BattleRoyale, Parkour }

public sealed class KillFeedEntry(string killer, string victim, string weapon, bool headshot, bool involvesPlayer)
{
    public string Killer { get; } = killer;
    public string Victim { get; } = victim;
    public string Weapon { get; } = weapon;
    public bool Headshot { get; } = headshot;
    public bool InvolvesPlayer { get; } = involvesPlayer;
    public float Age;
}

/// <summary>
/// Owns everything and runs the game loop:
///   1. read input           (every frame)
///   2. simulate physics/AI  (fixed 120 ticks per second, see Update)
///   3. draw 3D world, then 2D HUD on top
/// Modes:
///   Deathmatch    everyone vs everyone, respawns, first to ScoreToWin kills wins.
///   Battle royale everyone vs everyone, no respawns, shrinking zone, last one alive wins (bigger map).
///   Parkour       empty map, no bots: just movement.
/// Deathmatch and battle royale start with a countdown during which nobody can move or shoot.
/// </summary>
public sealed class Game
{
    public const int TickRate = 120;
    const float TickDt = 1f / TickRate;
    public const float BaseFov = 75f;
    const float RadiansPerPixel = 0.001f; // mouse turn per pixel at sensitivity 1.00
    const float KillFeedLifetime = 6f;
    public const int ScoreToWin = 20;      // deathmatch
    public const float CountdownTime = 5f; // seconds frozen before deathmatch / battle royale start
    public const float GoTime = 0.8f;      // how long "GO!" stays on screen afterwards

    readonly World arena = World.CreateArena();
    readonly World royale = World.CreateRoyale();
    public World World { get; private set; }
    public readonly Zone Zone = new();
    public readonly Player Player = new();
    public readonly List<Bot> Bots = [];
    public readonly List<ICombatant> Combatants = []; // the player + every bot
    public readonly Effects Effects = new();
    public readonly List<KillFeedEntry> KillFeed = [];

    public GameState State = GameState.Menu;
    public GameMode Mode = GameMode.Deathmatch;
    public Camera3D Camera;
    public float Sensitivity = 1.00f;
    public float RenderAlpha;
    public float HitmarkerTimer;
    public bool HitmarkerKill, HitmarkerHead;
    public float DamageFlash;
    public ICombatant? Winner;
    public ICombatant? KilledBy; // deathmatch: who eliminated you last; the death cam looks at them
    public Bot? Spectating;      // battle royale: who the camera follows after you are eliminated
    public float TopSpeed;       // parkour: best horizontal speed this run
    /// <summary>Above 0: match not started yet (frozen). 0 down to -GoTime: just started, "GO!" is shown.</summary>
    public float Countdown;

    float accumulator, menuTime;
    bool pendingCrouchPress, quit;
    int ignoreMouseFrames;
    Audio? audio; // null until the window exists (and in headless tests)

    static readonly (string Name, Color Color)[] BotRoster =
    [
        ("Sn1per", new Color(230, 80, 80, 255)),
        ("BhopKing", new Color(80, 140, 230, 255)),
        ("xXNoobXx", new Color(90, 200, 110, 255)),
        ("Pixel", new Color(230, 180, 60, 255)),
        ("Rusher", new Color(180, 90, 220, 255)),
        ("Camper", new Color(60, 200, 200, 255)),
        ("Turbo", new Color(240, 130, 40, 255)),
        ("Glitch", new Color(240, 100, 180, 255)),
        ("Nova", new Color(140, 220, 60, 255)),
        ("Zippy", new Color(100, 100, 240, 255)),
        ("Blocky", new Color(160, 120, 90, 255)),
    ];

    static readonly Color SkyColor = new(150, 200, 240, 255);
    static readonly Color PlayerTracer = new(255, 230, 150, 255);
    static readonly Color BotTracer = new(255, 120, 90, 255);

    public Game() => World = arena;
    public bool HasZone => Mode == GameMode.BattleRoyale;

    public int AliveCount
    {
        get
        {
            int n = 0;
            foreach (var c in Combatants) if (c.Alive) n++;
            return n;
        }
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1280, 720, "PewBlock");
        Raylib.SetWindowMinSize(800, 450);
        Raylib.SetExitKey(KeyboardKey.Null); // Esc pauses instead of quitting
        audio = new Audio(Path.Combine(AppContext.BaseDirectory, "source"), [WeaponDefs.Rifle.Sound, WeaponDefs.Sniper.Sound]);
        Textures.Load(Path.Combine(AppContext.BaseDirectory, "assets", "textures"));
        Camera = new Camera3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, BaseFov, CameraProjection.Perspective);
        ResetMatch(GameMode.Deathmatch); // bots standing around as a menu backdrop

        while (!quit && !Raylib.WindowShouldClose())
        {
            Update();
            Draw();
        }
        audio.Dispose();
        Textures.Unload();
        Raylib.CloseWindow();
    }

    // ───────────────────────────── match flow ─────────────────────────────

    void ResetMatch(GameMode mode)
    {
        Mode = mode;
        World = mode == GameMode.BattleRoyale ? royale : arena;
        Bots.Clear();
        if (mode != GameMode.Parkour)
            for (int i = 0; i < BotRoster.Length; i++) Bots.Add(new Bot(BotRoster[i].Name, BotRoster[i].Color, look: i));
        Combatants.Clear();
        Combatants.Add(Player);
        Combatants.AddRange(Bots);

        Zone.Reset(World);
        // Everyone starts on a different spawn point (there are as many as players).
        var spawns = World.SpawnPoints.ToArray();
        Random.Shared.Shuffle(spawns);
        Player.Kills = Player.Deaths = Player.Placement = 0;
        Player.WeaponIndex = 0;
        Player.Spawn(spawns[0]);
        for (int i = 0; i < Bots.Count; i++) Bots[i].Spawn(spawns[(i + 1) % spawns.Length], World);

        KillFeed.Clear();
        Effects.Clear();
        Winner = KilledBy = null;
        Spectating = null;
        TopSpeed = 0;
        Countdown = mode == GameMode.Parkour ? -GoTime : CountdownTime;
        accumulator = 0;
        DamageFlash = HitmarkerTimer = 0;
    }

    void StartMatch(GameMode mode)
    {
        ResetMatch(mode);
        Play();
    }

    void Play()
    {
        State = GameState.Playing;
        Raylib.DisableCursor();
        ignoreMouseFrames = 2; // the first mouse delta after locking the cursor is garbage
    }

    void Pause()
    {
        State = GameState.Paused;
        Raylib.EnableCursor();
    }

    void GoToMenu()
    {
        State = GameState.Menu;
        Raylib.EnableCursor();
    }

    void AddKill(string killer, string victim, string weapon, bool headshot, bool involvesPlayer)
    {
        KillFeed.Add(new KillFeedEntry(killer, victim, weapon, headshot, involvesPlayer));
        if (KillFeed.Count > 6) KillFeed.RemoveAt(0);
    }

    void CheckMatchEnd()
    {
        switch (Mode)
        {
            case GameMode.Deathmatch:
                Winner = Combatants.FirstOrDefault(c => c.Kills >= ScoreToWin);
                break;
            case GameMode.BattleRoyale:
                if (AliveCount > 1) return;
                // Last one standing wins. If the final players died on the same tick,
                // the last one eliminated already holds placement 1.
                Winner = Combatants.FirstOrDefault(c => c.Alive) ?? Combatants.First(c => c.Placement == 1);
                Winner.Placement = 1;
                break;
            case GameMode.Parkour:
                return; // no winner, play as long as you like
        }
        if (Winner == null) return;
        State = GameState.MatchOver;
        Raylib.EnableCursor();
    }

    /// <summary>
    /// All damage goes through here. <paramref name="attacker"/> is null for zone damage.
    /// Returns true if it eliminated the victim.
    /// </summary>
    bool ApplyDamage(ICombatant victim, float amount, ICombatant? attacker, string weapon, bool headshot)
    {
        if (victim is Bot bot && attacker != null) bot.NotifyAttacked(attacker);
        if (!victim.TakeDamage(amount)) return false;

        if (attacker != null) attacker.Kills++;
        victim.Deaths++;
        if (victim == Player) KilledBy = attacker;
        AddKill(attacker?.Name ?? "Zone", victim.Name, weapon, headshot, attacker == Player || victim == Player);
        var color = victim is Bot b ? b.Color : new Color(255, 60, 60, 255);
        Effects.Burst(victim.Feet + new Vector3(0, 1, 0), color, 30);

        if (Mode == GameMode.BattleRoyale)
        {
            victim.Placement = AliveCount + 1; // e.g. 3 still alive → you finished 4th
            // Spectate the killer when you (or the bot you're watching) get eliminated.
            if (victim == Player || victim == Spectating)
                Spectating = attacker is Bot { Alive: true } killer ? killer : NextAliveBot(Spectating);
        }
        else
        {
            victim.RespawnTimer = Combat.RespawnDelay;
        }
        return true;
    }

    /// <summary>Picks one of the 3 spawn points farthest from every living enemy of <paramref name="who"/>.</summary>
    Vector3 PickSpawn(ICombatant who)
    {
        float Threat(Vector3 spawn)
        {
            float nearest = float.MaxValue;
            foreach (var c in Combatants)
                if (c != who && c.Alive) nearest = MathF.Min(nearest, Vector3.Distance(spawn, c.Feet));
            return nearest;
        }
        var best = World.SpawnPoints.OrderByDescending(Threat).Take(3).ToArray();
        return best[Random.Shared.Next(best.Length)];
    }

    void Respawn(ICombatant who)
    {
        var at = PickSpawn(who);
        if (who is Bot bot) bot.Spawn(at, World);
        else Player.Spawn(at);
    }

    Bot? NextAliveBot(Bot? after)
    {
        int start = after == null ? -1 : Bots.IndexOf(after);
        for (int i = 1; i <= Bots.Count; i++)
        {
            var b = Bots[(start + i + Bots.Count) % Bots.Count];
            if (b.Alive) return b;
        }
        return null;
    }

    // ───────────────────────────── update ─────────────────────────────

    void Update()
    {
        float dt = MathF.Min(Raylib.GetFrameTime(), 0.1f);
        switch (State)
        {
            case GameState.Menu:
                menuTime += dt;
                UpdateMenu();
                break;
            case GameState.Paused:
                if (Raylib.IsMouseButtonPressed(MouseButton.Left)) Play();
                if (Raylib.IsKeyPressed(KeyboardKey.M)) GoToMenu();
                if (Raylib.IsKeyPressed(KeyboardKey.Q)) quit = true;
                if (Raylib.IsKeyPressed(KeyboardKey.LeftBracket)) Sensitivity = MathF.Max(0.1f, Sensitivity - 0.1f);
                if (Raylib.IsKeyPressed(KeyboardKey.RightBracket)) Sensitivity += 0.1f;
                break;
            case GameState.MatchOver:
                if (Raylib.IsKeyPressed(KeyboardKey.R)) StartMatch(Mode);
                if (Raylib.IsKeyPressed(KeyboardKey.M)) GoToMenu();
                if (Raylib.IsKeyPressed(KeyboardKey.Q)) quit = true;
                break;
            case GameState.Playing:
                UpdatePlaying(dt);
                break;
        }
        UpdateCamera();
    }

    /// <summary>Click a mode card, or press 1 / 2 / 3.</summary>
    void UpdateMenu()
    {
        int choice = -1;
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            choice = Hud.MenuCardAt(Raylib.GetMousePosition(), Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        if (Raylib.IsKeyPressed(KeyboardKey.One)) choice = 0;
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) choice = 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Three)) choice = 2;
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) quit = true;
        if (choice >= 0) StartMatch((GameMode)choice);
    }

    void UpdatePlaying(float dt)
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { Pause(); return; }

        // Pre-match countdown: you can look around, but nobody moves or shoots yet.
        Countdown = MathF.Max(-GoTime, Countdown - dt);
        bool frozen = Countdown > 0;

        // Mouse look runs every frame (not in fixed ticks) so aiming has zero added latency.
        // Sensitivity scales with FOV so scoped aim doesn't feel twitchy.
        var mouse = Raylib.GetMouseDelta();
        if (ignoreMouseFrames > 0) { ignoreMouseFrames--; mouse = Vector2.Zero; }

        if (Player.Alive)
        {
            Player.Look(mouse, Sensitivity * RadiansPerPixel * Camera.FovY / BaseFov);
            if (!frozen) HandleWeaponInput();
        }
        else if (Mode == GameMode.BattleRoyale && HandleSpectatorInput())
        {
            return; // started a new match
        }
        Player.UpdateFrame(dt);

        // Fixed timestep: physics always advances in identical 1/120 s steps no matter
        // the frame rate, so jumps, speed and collisions behave the same on every PC.
        pendingCrouchPress |= Raylib.IsKeyPressed(KeyboardKey.LeftShift) || Raylib.IsKeyPressed(KeyboardKey.C);
        var input = ReadMoveInput();
        accumulator += dt;
        while (accumulator >= TickDt)
        {
            input.CrouchPressed = pendingCrouchPress;
            pendingCrouchPress = false;
            // During the countdown only gravity runs (so you land on the ground), with no input and no bots.
            if (frozen) Player.Tick(World, default, TickDt);
            else FixedTick(input);
            accumulator -= TickDt;
        }
        RenderAlpha = accumulator / TickDt; // how far we are between the last tick and the next

        Effects.Update(dt);
        HitmarkerTimer = MathF.Max(0, HitmarkerTimer - dt);
        DamageFlash = MathF.Max(0, DamageFlash - dt * 1.5f);
        foreach (var k in KillFeed) k.Age += dt;
        KillFeed.RemoveAll(k => k.Age > KillFeedLifetime);
        CheckMatchEnd();
    }

    /// <summary>Battle royale, after elimination: click to watch the next player, R to start a new match.</summary>
    bool HandleSpectatorInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.R)) { StartMatch(Mode); return true; }
        if (Spectating is not { Alive: true } || Raylib.IsMouseButtonPressed(MouseButton.Left))
            Spectating = NextAliveBot(Spectating);
        return false;
    }

    static MoveInput ReadMoveInput()
    {
        static float Axis(KeyboardKey pos, KeyboardKey neg) =>
            (Raylib.IsKeyDown(pos) ? 1 : 0) - (Raylib.IsKeyDown(neg) ? 1 : 0);
        return new MoveInput
        {
            Forward = Axis(KeyboardKey.W, KeyboardKey.S),
            Right = Axis(KeyboardKey.D, KeyboardKey.A),
            JumpHeld = Raylib.IsKeyDown(KeyboardKey.Space),
            CrouchHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.C),
        };
    }

    void FixedTick(MoveInput input)
    {
        Player.Tick(World, input, TickDt);
        TopSpeed = MathF.Max(TopSpeed, Player.HorizontalSpeed);

        var zone = HasZone ? Zone : null;
        foreach (var bot in Bots)
            if (bot.Tick(World, Combatants, zone, TickDt) is { } shot) HandleBotShot(bot, shot);

        switch (Mode)
        {
            case GameMode.Deathmatch:
                foreach (var c in Combatants)
                {
                    if (c.Alive) continue;
                    c.RespawnTimer -= TickDt;
                    if (c.RespawnTimer <= 0) Respawn(c);
                }
                break;
            case GameMode.BattleRoyale:
                Zone.Update(TickDt);
                foreach (var c in Combatants)
                    if (c.Alive && !Zone.Contains(c.Feet)) ApplyDamage(c, Zone.Dps * TickDt, null, "Zone", false);
                break;
        }
    }

    void HandleBotShot(Bot bot, BotShot shot)
    {
        Effects.AddTracer(shot.From, shot.To, BotTracer);
        audio?.PlayShotAt(Bot.Weapon.Sound, shot.From, Camera.Position, Vector3.Normalize(Camera.Target - Camera.Position));
        if (!shot.Hit) return;
        if (shot.Target == Player) DamageFlash = MathF.Min(0.6f, DamageFlash + 0.25f);
        ApplyDamage(shot.Target, shot.Damage, bot, Bot.Weapon.Name, false);
    }

    // ───────────────────────────── shooting ─────────────────────────────

    void HandleWeaponInput()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.One)) Player.SwitchWeapon(0);
        if (Raylib.IsKeyPressed(KeyboardKey.Two)) Player.SwitchWeapon(1);
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0) Player.SwitchWeapon((Player.WeaponIndex + 1) % Player.Weapons.Length);
        if (Raylib.IsKeyPressed(KeyboardKey.R)) Player.Weapon.StartReload();

        Player.WantsAds = Raylib.IsMouseButtonDown(MouseButton.Right);
        bool trigger = Player.Weapon.Def.Automatic
            ? Raylib.IsMouseButtonDown(MouseButton.Left)
            : Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (!trigger) return;
        if (Player.CanShoot) FireHitscan();
        else if (Player.Weapon.Ammo == 0) Player.Weapon.StartReload();
    }

    /// <summary>
    /// "Hitscan": bullets travel instantly. Cast a ray from the eye, find the
    /// nearest thing it touches (wall, bot head, bot body), apply damage.
    /// </summary>
    void FireHitscan()
    {
        var weapon = Player.Weapon;
        weapon.Fire();
        audio?.PlayOwnShot(weapon.Def.Sound);

        var eye = Player.EyePosition(RenderAlpha);
        var forward = Player.AimForward;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);

        // Random point inside the spread cone (sqrt keeps it uniform over the disk).
        float spread = Player.CurrentSpread();
        float angle = MathUtil.Range(0, MathF.Tau);
        float radius = MathF.Sqrt(Random.Shared.NextSingle()) * spread;
        var dir = Vector3.Normalize(forward + right * (MathF.Cos(angle) * radius) + up * (MathF.Sin(angle) * radius));
        var ray = new Ray(eye, dir);

        float best = 300f;
        var end = eye + dir * best;
        bool hitWorld = World.Raycast(ray, best, out var wallHit);
        if (hitWorld) { best = wallHit.Distance; end = wallHit.Point; }

        Bot? target = null;
        bool headshot = false;
        foreach (var bot in Bots)
        {
            if (!bot.Alive) continue;
            var head = Raylib.GetRayCollisionBox(ray, bot.HeadBox);
            if (head.Hit && head.Distance < best) { best = head.Distance; end = head.Point; target = bot; headshot = true; }
            var body = Raylib.GetRayCollisionBox(ray, bot.BodyBox);
            if (body.Hit && body.Distance < best) { best = body.Distance; end = body.Point; target = bot; headshot = false; }
        }

        if (target != null)
        {
            float damage = weapon.Def.Damage * (headshot ? weapon.Def.HeadshotMultiplier : 1f);
            bool killed = ApplyDamage(target, damage, Player, weapon.Def.Name, headshot);
            HitmarkerTimer = 0.25f;
            HitmarkerKill = killed;
            HitmarkerHead = headshot;
            Effects.Burst(end, new Color(220, 40, 40, 255), 8, 3f);
        }
        else if (hitWorld)
        {
            Effects.Burst(end + wallHit.Normal * 0.02f, new Color(90, 90, 90, 255), 5, 2f);
        }

        Effects.AddTracer(Player.MuzzlePosition(eye), end, PlayerTracer);
        Player.ApplyRecoil();
        if (weapon.Ammo == 0) weapon.StartReload();
    }

    // ───────────────────────────── drawing ─────────────────────────────

    void UpdateCamera()
    {
        if (State == GameState.Menu)
        {
            float t = menuTime * 0.12f;
            Camera.Position = new Vector3(MathF.Sin(t) * 46, 24, MathF.Cos(t) * 46);
            Camera.Target = Vector3.Zero;
            Camera.FovY = BaseFov;
            return;
        }

        if (!Player.Alive && Mode == GameMode.BattleRoyale && Spectating != null)
        {
            // Third-person chase camera behind the spectated bot, pulled in if a wall is in the way.
            var head = Spectating.RenderPosition(RenderAlpha) + new Vector3(0, 1.6f, 0);
            var fwd = MathUtil.FlatForward(Spectating.Yaw);
            var back = Vector3.Normalize(-fwd * 3.2f + new Vector3(0, 1.2f, 0));
            float dist = 3.4f;
            if (World.Raycast(new Ray(head, back), dist, out var hit)) dist = MathF.Max(0.3f, hit.Distance - 0.3f);
            Camera.Position = head + back * dist;
            Camera.Target = head + fwd * 2f;
            Camera.FovY = BaseFov;
            return;
        }

        var eye = Player.EyePosition(RenderAlpha);
        if (!Player.Alive)
        {
            // Death cam: float up from where you died and watch whoever got you.
            eye.Y += (Combat.RespawnDelay - Player.RespawnTimer) * 0.8f;
            Camera.Position = eye;
            Camera.Target = KilledBy is { Alive: true } killer ? killer.Eye : eye + Player.AimForward;
            Camera.FovY = BaseFov;
            return;
        }

        Camera.Position = eye;
        Camera.Target = eye + Player.AimForward;
        float ads = Player.AdsT * Player.AdsT * (3 - 2 * Player.AdsT);
        Camera.FovY = float.Lerp(BaseFov, Player.Weapon.Def.AdsFov, ads);
    }

    void Draw()
    {
        Raylib.BeginDrawing();
        Raylib.ClearBackground(SkyColor);

        Raylib.BeginMode3D(Camera);
        World.Draw();
        foreach (var bot in Bots) bot.Draw(RenderAlpha);
        Effects.Draw();
        bool inGame = State != GameState.Menu;
        if (inGame && HasZone) Zone.Draw(); // translucent, so drawn after solid things
        if (inGame) Player.DrawViewModel(Camera.Position);
        Raylib.EndMode3D();

        Hud.Draw(this);
        Raylib.EndDrawing();
    }
}
