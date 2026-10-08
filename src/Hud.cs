using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace PewBlock;

/// <summary>Everything drawn in 2D screen space on top of the 3D view.</summary>
public static class Hud
{
    static readonly Color Shadow = new(0, 0, 0, 160);
    static readonly Color Panel = new(0, 0, 0, 120);
    static readonly Color Gold = new(255, 210, 90, 255);
    static readonly Color Red = new(255, 80, 70, 255);
    static readonly Color Purple = new(180, 120, 255, 255);

    /// <summary>Menu cards, in <see cref="GameMode"/> order.</summary>
    static readonly (string Title, string Line1, string Line2)[] ModeCards =
    [
        ("DEATHMATCH", "12 players, everyone vs everyone", $"respawns, first to {Game.ScoreToWin} kills"),
        ("BATTLE ROYALE", "12 players, big map, no respawns", "shrinking zone, last one standing"),
        ("PARKOUR", "empty map, no enemies", "practice bhop, slides and jumps"),
    ];

    public static void Draw(Game g)
    {
        int w = GetScreenWidth(), h = GetScreenHeight();
        float s = h / 720f;

        if (g.State == GameState.Menu)
        {
            DrawMenu(w, h, s);
            return;
        }

        var p = g.Player;
        DrawNameTags(g, s);
        if (p.Alive)
        {
            if (p.Weapon.Def.HasScope && p.AdsT > 0.9f) DrawScope(w, h);
            else DrawCrosshair(g, w, h, s);
            DrawHitmarker(g, w, h, s);
            DrawVignette(w, h, new Color(200, 0, 0, 255), g.DamageFlash + (p.Health < 35 ? (1 - p.Health / 35f) * 0.35f : 0));
            if (g.HasZone && !g.Zone.Contains(p.Feet))
            {
                DrawVignette(w, h, new Color(120, 60, 255, 255), 0.45f);
                TextCentered($"OUTSIDE THE ZONE  -{g.Zone.Dps:0} HP/s", w / 2, (int)(h * 0.22f), (int)(28 * s), Purple);
            }
            DrawStatus(p, w, h, s);
        }
        else if (g.State != GameState.MatchOver)
        {
            if (g.Mode == GameMode.BattleRoyale) DrawSpectator(g, w, h, s);
            else DrawDeathScreen(g, w, h, s);
        }
        if (g.Mode != GameMode.Parkour) DrawKillFeed(g, w, s);
        DrawTopBar(g, w, s);
        if (g.State != GameState.MatchOver && g.Countdown > -Game.GoTime) DrawCountdown(g, w, h, s);
        DrawMinimap(g, s);

        if (g.State == GameState.MatchOver) DrawMatchOver(g, w, h, s);
        else if (IsKeyDown(KeyboardKey.Tab) && g.Mode != GameMode.Parkour) DrawScoreboard(g, w, h, s);
        if (g.State == GameState.Paused) DrawPause(g, w, h, s);

        DrawFPS(10, 10);
    }

    // ── text helpers ──
    static void Text(string t, int x, int y, int size, Color c)
    {
        DrawText(t, x + 2, y + 2, size, Shadow);
        DrawText(t, x, y, size, c);
    }

    static void TextCentered(string t, int cx, int y, int size, Color c) => Text(t, cx - MeasureText(t, size) / 2, y, size, c);
    static void TextRight(string t, int right, int y, int size, Color c) => Text(t, right - MeasureText(t, size), y, size, c);

    // ── aiming ──
    static void DrawCrosshair(Game g, int w, int h, float s)
    {
        // Convert the spread angle into pixels, so the crosshair shows the real cone.
        float halfFov = g.Camera.FovY * 0.5f * MathUtil.Deg2Rad;
        float gap = 4 * s + MathF.Tan(g.Player.CurrentSpread()) / MathF.Tan(halfFov) * (h * 0.5f);
        float len = 9 * s, th = 2 * s;
        var c = new Vector2(w / 2f, h / 2f);
        DrawLineEx(c with { X = c.X - gap - len }, c with { X = c.X - gap }, th, Color.White);
        DrawLineEx(c with { X = c.X + gap }, c with { X = c.X + gap + len }, th, Color.White);
        DrawLineEx(c with { Y = c.Y - gap - len }, c with { Y = c.Y - gap }, th, Color.White);
        DrawLineEx(c with { Y = c.Y + gap }, c with { Y = c.Y + gap + len }, th, Color.White);
        DrawCircleV(c, 1.5f * s, Color.White);
    }

    static void DrawScope(int w, int h)
    {
        var c = new Vector2(w / 2f, h / 2f);
        float r = h * 0.46f;
        DrawRing(c, r, MathF.Max(w, h), 0, 360, 96, Color.Black);
        DrawRing(c, r - 4, r, 0, 360, 96, new Color(30, 30, 30, 255));
        DrawLineEx(c with { X = c.X - r }, c with { X = c.X + r }, 2, Color.Black);
        DrawLineEx(c with { Y = c.Y - r }, c with { Y = c.Y + r }, 2, Color.Black);
        DrawCircleV(c, 3, Color.Red);
    }

    static void DrawHitmarker(Game g, int w, int h, float s)
    {
        if (g.HitmarkerTimer <= 0) return;
        var color = g.HitmarkerKill ? Red : g.HitmarkerHead ? Gold : Color.White;
        color = ColorAlpha(color, g.HitmarkerTimer / 0.25f);
        float a = 6 * s, b = 14 * s;
        var c = new Vector2(w / 2f, h / 2f);
        foreach (var (dx, dy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            DrawLineEx(c + new Vector2(dx * a, dy * a), c + new Vector2(dx * b, dy * b), 2.5f * s, color);
    }

    /// <summary>Colored gradient around the screen edges (damage, zone).</summary>
    static void DrawVignette(int w, int h, Color color, float alpha)
    {
        float a = Math.Clamp(alpha, 0, 0.8f);
        if (a <= 0) return;
        var edge = color with { A = (byte)(a * 255) };
        var clear = color with { A = 0 };
        int e = (int)(h * 0.25f);
        DrawRectangleGradientV(0, 0, w, e, edge, clear);
        DrawRectangleGradientV(0, h - e, w, e, clear, edge);
        DrawRectangleGradientH(0, 0, e, h, edge, clear);
        DrawRectangleGradientH(w - e, 0, e, h, clear, edge);
    }

    // ── status ──
    static void DrawStatus(Player p, int w, int h, float s)
    {
        // Health bar (bottom left)
        int bx = (int)(20 * s), by = h - (int)(50 * s), bw = (int)(260 * s), bh = (int)(26 * s);
        DrawRectangle(bx, by, bw, bh, Panel);
        float frac = p.Health / Combat.MaxHealth;
        var hc = frac > 0.5f ? new Color(90, 220, 110, 255) : frac > 0.25f ? Gold : Red;
        DrawRectangle(bx + 3, by + 3, (int)((bw - 6) * frac), bh - 6, hc);
        Text($"{MathF.Ceiling(p.Health)}", bx + bw + (int)(10 * s), by, (int)(26 * s), Color.White);

        // Ammo (bottom right)
        var weapon = p.Weapon;
        int right = w - (int)(24 * s);
        TextRight($"{weapon.Ammo}", right - MeasureText($" / {weapon.Def.MagazineSize}", (int)(24 * s)), h - (int)(62 * s), (int)(44 * s), Color.White);
        TextRight($" / {weapon.Def.MagazineSize}", right, h - (int)(46 * s), (int)(24 * s), new Color(200, 200, 200, 255));
        TextRight(weapon.Def.Name.ToUpperInvariant(), right, h - (int)(90 * s), (int)(20 * s), Gold);
        if (weapon.IsReloading)
        {
            int rw = (int)(160 * s);
            TextCentered("RELOADING", w / 2, h / 2 + (int)(40 * s), (int)(20 * s), Color.White);
            DrawRectangle(w / 2 - rw / 2, h / 2 + (int)(66 * s), rw, (int)(6 * s), Panel);
            DrawRectangle(w / 2 - rw / 2, h / 2 + (int)(66 * s), (int)(rw * weapon.ReloadProgress), (int)(6 * s), Color.White);
        }

        // Speed meter (bottom center) - useful while learning to bhop.
        string state = p.Sliding ? "  SLIDE" : !p.Body.Grounded ? "  AIR" : "";
        TextCentered($"{p.HorizontalSpeed:0.0} m/s{state}", w / 2, h - (int)(40 * s), (int)(22 * s), Color.White);
    }

    /// <summary>Deathmatch: shown while waiting to respawn.</summary>
    static void DrawDeathScreen(Game g, int w, int h, float s)
    {
        string killer = g.KilledBy?.Name ?? "?";
        TextCentered($"KILLED BY {killer.ToUpperInvariant()}", w / 2, (int)(h * 0.36f), (int)(34 * s), Red);
        TextCentered($"respawning in {MathF.Max(0, g.Player.RespawnTimer):0.0}s", w / 2, (int)(h * 0.36f + 44 * s), (int)(22 * s), Color.White);
    }

    /// <summary>Battle royale: shown after you're eliminated.</summary>
    static void DrawSpectator(Game g, int w, int h, float s)
    {
        TextCentered($"ELIMINATED  -  #{g.Player.Placement} of {g.Combatants.Count}", w / 2, (int)(h * 0.14f), (int)(32 * s), Red);
        if (g.Spectating is { } bot)
        {
            TextCentered($"SPECTATING {bot.Name}   {MathF.Ceiling(bot.Health)} HP   {bot.Kills} kills",
                w / 2, h - (int)(76 * s), (int)(24 * s), Color.White);
        }
        TextCentered("CLICK  next player      R  new match", w / 2, h - (int)(42 * s), (int)(20 * s), Gold);
    }

    static void DrawTopBar(Game g, int w, float s)
    {
        string line;
        var color = Color.White;
        switch (g.Mode)
        {
            case GameMode.Deathmatch:
                var leader = g.Combatants.MaxBy(c => c.Kills)!;
                line = $"DEATHMATCH   |   YOU {g.Player.Kills}   |   LEADER: {leader.Name} {leader.Kills}   |   FIRST TO {Game.ScoreToWin}";
                break;
            case GameMode.BattleRoyale:
                line = $"BATTLE ROYALE   |   ALIVE {g.AliveCount}   |   KILLS {g.Player.Kills}   |   {g.Zone.Status}";
                if (g.Zone.Shrinking) color = Purple;
                break;
            default:
                line = $"PARKOUR   |   TOP SPEED {g.TopSpeed:0.0} m/s   |   ESC  menu";
                break;
        }
        int size = (int)(20 * s);
        int tw = MeasureText(line, size);
        DrawRectangle(w / 2 - tw / 2 - 12, (int)(8 * s), tw + 24, size + (int)(10 * s), Panel);
        TextCentered(line, w / 2, (int)(13 * s), size, color);
    }

    /// <summary>Big 5, 4, 3, 2, 1 before the match, then a fading "GO!".</summary>
    static void DrawCountdown(Game g, int w, int h, float s)
    {
        if (g.Countdown <= 0)
        {
            float fade = 1 + g.Countdown / Game.GoTime; // 1 → 0
            int goSize = (int)(110 * s * (1.4f - 0.4f * fade));
            int x = w / 2 - MeasureText("GO!", goSize) / 2, y = (int)(h * 0.3f);
            DrawText("GO!", x + 3, y + 3, goSize, ColorAlpha(Shadow, Shadow.A / 255f * fade));
            DrawText("GO!", x, y, goSize, ColorAlpha(Gold, fade));
            return;
        }

        DrawRectangle(0, (int)(h * 0.2f), w, (int)(h * 0.36f), Panel);
        TextCentered("MATCH STARTS IN", w / 2, (int)(h * 0.23f), (int)(28 * s), Color.White);
        // Each number starts big and shrinks during its second.
        float sub = g.Countdown - MathF.Floor(g.Countdown);
        int size = (int)(110 * s * (1 + 0.35f * sub));
        TextCentered($"{MathF.Ceiling(g.Countdown)}", w / 2, (int)(h * 0.38f) - size / 2, size, Gold);
        string goal = g.Mode == GameMode.BattleRoyale
            ? "Last one standing wins. Stay inside the zone!"
            : $"First to {Game.ScoreToWin} kills wins";
        TextCentered(goal, w / 2, (int)(h * 0.5f), (int)(22 * s), Color.White);
    }

    static void DrawKillFeed(Game g, int w, float s)
    {
        int size = (int)(18 * s);
        int y = (int)(50 * s);
        for (int i = g.KillFeed.Count - 1; i >= 0; i--)
        {
            var k = g.KillFeed[i];
            string text = $"{k.Killer}  [{k.Weapon}{(k.Headshot ? " HS" : "")}]  {k.Victim}";
            int tw = MeasureText(text, size);
            int x = w - tw - (int)(20 * s);
            DrawRectangle(x - 8, y - 4, tw + 16, size + 8, k.InvolvesPlayer ? new Color(120, 30, 30, 150) : Panel);
            Text(text, x, y, size, Color.White);
            y += size + (int)(12 * s);
        }
    }

    /// <summary>
    /// Top-down map of the blocks and you (enemies are not shown). In battle royale it
    /// also shows the current zone (purple) and the next one (white).
    /// Viewed from above, +Z is up and +X is to the LEFT (that's how a right-handed,
    /// Y-up world looks from above), hence the minus signs in ToMap.
    /// </summary>
    static void DrawMinimap(Game g, float s)
    {
        int size = (int)(170 * s);
        int x0 = 10, y0 = (int)(36 * s);
        float scale = size / (g.World.HalfSize * 2);
        var center = new Vector2(x0 + size / 2f, y0 + size / 2f);
        Vector2 ToMap(float x, float z) => center + new Vector2(-x * scale, -z * scale);

        DrawRectangle(x0, y0, size, size, new Color(30, 35, 45, 200));
        BeginScissorMode(x0, y0, size, size);
        foreach (var b in g.World.Blocks)
        {
            if (b.Box.Max.Y <= 0) continue; // floor
            var tl = ToMap(b.Box.Max.X, b.Box.Max.Z);
            DrawRectangleV(tl, new Vector2(b.Box.Max.X - b.Box.Min.X, b.Box.Max.Z - b.Box.Min.Z) * scale, ColorAlpha(b.Color, 0.8f));
        }
        if (g.HasZone)
        {
            var z = g.Zone;
            DrawRing(ToMap(z.NextCenter.X, z.NextCenter.Y), z.NextRadius * scale - 1, z.NextRadius * scale + 1, 0, 360, 64, Color.White);
            DrawRing(ToMap(z.Center.X, z.Center.Y), z.Radius * scale - 1.5f, z.Radius * scale + 1.5f, 0, 360, 64, Purple);
        }

        // You, or whoever you're spectating. The line shows the facing direction.
        (Vector3 pos, float yaw) = g.Player.Alive || g.Spectating == null
            ? (g.Player.Feet, g.Player.Yaw)
            : (g.Spectating.Feet, g.Spectating.Yaw);
        var p = ToMap(pos.X, pos.Z);
        var dir = new Vector2(-MathF.Sin(yaw), -MathF.Cos(yaw));
        DrawLineEx(p, p + dir * 10 * s, 2 * s, Gold);
        DrawCircleV(p, 3.5f * s, Gold);
        EndScissorMode();
        DrawRectangleLines(x0, y0, size, size, new Color(255, 255, 255, 120));
    }

    static void DrawNameTags(Game g, float s)
    {
        var camForward = Vector3.Normalize(g.Camera.Target - g.Camera.Position);
        foreach (var bot in g.Bots)
        {
            if (!bot.Alive) continue;
            var head = bot.RenderPosition(g.RenderAlpha) + new Vector3(0, 2.15f, 0);
            var toHead = head - g.Camera.Position;
            if (Vector3.Dot(toHead, camForward) <= 0 || toHead.Length() > 60) continue;
            if (!g.World.LineOfSight(g.Camera.Position, head - new Vector3(0, 0.4f, 0))) continue;

            var sp = GetWorldToScreen(head, g.Camera);
            int size = (int)(16 * s);
            TextCentered(bot.Name, (int)sp.X, (int)sp.Y - size - 8, size, Color.White);
            int bw = (int)(50 * s), bh = (int)(5 * s);
            DrawRectangle((int)sp.X - bw / 2, (int)sp.Y - 4, bw, bh, Panel);
            DrawRectangle((int)sp.X - bw / 2, (int)sp.Y - 4, (int)(bw * bot.Health / Combat.MaxHealth), bh, Red);
        }
    }

    // ── menus ──

    /// <summary>Screen rectangle of menu card <paramref name="i"/>. Shared by drawing and click detection.</summary>
    public static Rectangle MenuCard(int i, int w, int h)
    {
        float s = h / 720f;
        float cw = 300 * s, ch = 120 * s, gap = 28 * s;
        float x = w / 2f - (ModeCards.Length * cw + (ModeCards.Length - 1) * gap) / 2 + i * (cw + gap);
        return new Rectangle(x, h * 0.34f, cw, ch);
    }

    /// <summary>Index of the card under <paramref name="mouse"/>, or -1.</summary>
    public static int MenuCardAt(Vector2 mouse, int w, int h)
    {
        for (int i = 0; i < ModeCards.Length; i++)
            if (CheckCollisionPointRec(mouse, MenuCard(i, w, h))) return i;
        return -1;
    }

    static void DrawMenu(int w, int h, float s)
    {
        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 90));
        TextCentered("PEWBLOCK", w / 2, (int)(h * 0.1f), (int)(72 * s), Gold);
        TextCentered("a Krunker-style FPS in C# + raylib", w / 2, (int)(h * 0.1f + 80 * s), (int)(22 * s), Color.White);

        int hovered = MenuCardAt(GetMousePosition(), w, h);
        for (int i = 0; i < ModeCards.Length; i++)
        {
            var r = MenuCard(i, w, h);
            var (title, line1, line2) = ModeCards[i];
            bool hot = i == hovered;
            DrawRectangleRec(r, hot ? new Color(60, 50, 20, 230) : new Color(20, 25, 35, 210));
            DrawRectangleLinesEx(r, hot ? 3 * s : 2 * s, hot ? Gold : new Color(255, 255, 255, 120));
            int cx = (int)(r.X + r.Width / 2);
            TextCentered($"{i + 1}  {title}", cx, (int)(r.Y + 14 * s), (int)(26 * s), hot ? Gold : Color.White);
            TextCentered(line1, cx, (int)(r.Y + 58 * s), (int)(17 * s), new Color(220, 220, 220, 255));
            TextCentered(line2, cx, (int)(r.Y + 84 * s), (int)(17 * s), new Color(220, 220, 220, 255));
        }

        string[] controls =
        [
            "WASD  move     MOUSE  aim     LMB  shoot     RMB  aim / scope",
            "SPACE  jump (hold to bunny hop)     SHIFT / C  crouch, slide while running",
            "R  reload     1 / 2 / WHEEL  switch weapon     TAB  scoreboard     ESC  pause",
        ];
        int y = (int)(h * 0.34f + 150 * s);
        foreach (var line in controls)
        {
            TextCentered(line, w / 2, y, (int)(18 * s), Color.White);
            y += (int)(28 * s);
        }
        TextCentered("CLICK A MODE  or press 1 / 2 / 3", w / 2, (int)(h * 0.88f), (int)(30 * s), Gold);
    }

    static void DrawPause(Game g, int w, int h, float s)
    {
        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 150));
        TextCentered("PAUSED", w / 2, (int)(h * 0.3f), (int)(56 * s), Color.White);
        TextCentered("Click to resume", w / 2, (int)(h * 0.45f), (int)(24 * s), Gold);
        TextCentered($"Sensitivity: {g.Sensitivity:0.00}   ( [  and  ] to change )", w / 2, (int)(h * 0.53f), (int)(22 * s), Color.White);
        TextCentered("M  main menu      Q  quit", w / 2, (int)(h * 0.6f), (int)(22 * s), Color.White);
    }

    static void DrawMatchOver(Game g, int w, int h, float s)
    {
        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 150));
        bool won = g.Winner == g.Player;
        bool royale = g.Mode == GameMode.BattleRoyale;
        string title = won ? (royale ? "VICTORY ROYALE" : "VICTORY") : $"{g.Winner?.Name.ToUpperInvariant()} WINS";
        TextCentered(title, w / 2, (int)(h * 0.06f), (int)(60 * s), won ? Gold : Red);
        if (!won)
        {
            int rank = royale ? g.Player.Placement : 1 + g.Combatants.Count(c => c.Kills > g.Player.Kills);
            string sub = royale ? $"You placed #{rank}" : $"You finished #{rank} with {g.Player.Kills} kills";
            TextCentered(sub, w / 2, (int)(h * 0.06f + 66 * s), (int)(26 * s), Color.White);
        }
        DrawScoreboard(g, w, h, s);
        TextCentered("R  play again     M  main menu     Q  quit", w / 2, (int)(h * 0.92f), (int)(24 * s), Color.White);
    }

    /// <summary>Deathmatch: kills and deaths. Battle royale: kills and finishing place.</summary>
    static void DrawScoreboard(Game g, int w, int h, float s)
    {
        bool royale = g.Mode == GameMode.BattleRoyale;
        var rows = royale
            // Alive players first (most kills on top), then eliminated ones by finishing position.
            ? g.Combatants.OrderBy(c => c.Alive ? 0 : 1).ThenByDescending(c => c.Alive ? c.Kills : 0).ThenBy(c => c.Placement).ToList()
            // Most kills on top; fewer deaths breaks ties.
            : g.Combatants.OrderByDescending(c => c.Kills).ThenBy(c => c.Deaths).ToList();

        int size = (int)(20 * s), rowH = (int)(27 * s);
        int pw = (int)(460 * s), ph = rowH * (rows.Count + 1) + (int)(14 * s);
        int x = w / 2 - pw / 2, y = (int)(h * 0.19f);
        DrawRectangle(x, y, pw, ph, new Color(20, 20, 30, 210));
        Text("PLAYER", x + 20, y + 8, size, Gold);
        TextRight("KILLS", x + pw - 130, y + 8, size, Gold);
        TextRight(royale ? "PLACE" : "DEATHS", x + pw - 20, y + 8, size, Gold);
        y += rowH;
        foreach (var c in rows)
        {
            var color = c == g.Player ? Gold : royale && !c.Alive ? new Color(150, 150, 150, 255) : Color.White;
            string last = royale ? (c.Placement > 0 ? $"#{c.Placement}" : "ALIVE") : $"{c.Deaths}";
            Text(c.Name, x + 20, y + 8, size, color);
            TextRight($"{c.Kills}", x + pw - 130, y + 8, size, color);
            TextRight(last, x + pw - 20, y + 8, size, color);
            y += rowH;
        }
    }
}
