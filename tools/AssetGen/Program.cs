// Generates the game's textures as grayscale PNGs in assets/textures.
//
// They are "detail maps": the game multiplies each one by the color of the block
// or body part it covers, so one image serves every color (every bot's uniform
// shares fabric.png, tinted red, blue, ...). Every texture tiles seamlessly,
// because all noise repeats exactly at the image edges.
//
// Run from anywhere inside the repo:   dotnet run --project tools/AssetGen
// Then commit the PNGs. Change a recipe below and rerun to restyle the game.

using Raylib_cs;

Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
string outDir = Path.Combine(FindRepoRoot(), "assets", "textures");
Directory.CreateDirectory(outDir);

// Name, size in pixels, recipe. How many meters one image covers is decided by the game (src/Textures.cs).
(string Name, int Size, Action<Canvas, Random> Paint)[] textures =
[
    ("floor_grass", 256, Recipes.Grass),
    ("floor_sand", 256, Recipes.Sand),
    ("concrete", 256, Recipes.Concrete),
    ("plaster", 256, Recipes.Plaster),
    ("brick", 256, Recipes.Brick),
    ("crate", 256, Recipes.Crate),
    ("metal_plate", 256, Recipes.MetalPlate),
    ("painted_metal", 128, Recipes.PaintedMetal),
    ("rock", 256, Recipes.Rock),
    ("bark", 128, Recipes.Bark),
    ("leaves", 256, Recipes.Leaves),
    ("sandbag", 128, Recipes.Sandbag),
    ("fabric", 128, Recipes.Fabric),
    ("leather", 128, Recipes.Leather),
    ("skin", 64, Recipes.Skin),
    ("hair", 64, Recipes.Hair),
    ("gun_metal", 128, Recipes.GunMetal),
    ("polymer", 128, Recipes.Polymer),
];

foreach (var (name, size, paint) in textures)
{
    var canvas = new Canvas(size);
    paint(canvas, new Random(name.Length * 7919 + name[0])); // fixed seed per texture: same output every run
    string path = Path.Combine(outDir, name + ".png");
    canvas.Save(path);
    Console.WriteLine($"{name}.png  {size}x{size}");
}
Console.WriteLine($"Wrote {textures.Length} textures to {outDir}");

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "PewBlock.csproj"))) return dir.FullName;
    throw new InvalidOperationException("Run this from inside the PewBlock repository.");
}

/// <summary>Grayscale image of floats (0 = black, 1 = white). Pixel access wraps around, so shapes drawn across an edge continue on the other side.</summary>
sealed class Canvas(int size)
{
    public readonly int Size = size;
    readonly float[] pixels = new float[size * size];

    int Wrap(int i) => ((i % Size) + Size) % Size;

    public float this[int x, int y]
    {
        get => pixels[Wrap(y) * Size + Wrap(x)];
        set => pixels[Wrap(y) * Size + Wrap(x)] = value;
    }

    /// <summary>Sets every pixel from (u, v) in 0..1 (pixel centers) and the integer pixel position.</summary>
    public void Fill(Func<float, float, int, int, float> f)
    {
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = f((x + 0.5f) / Size, (y + 0.5f) / Size, x, y);
    }

    /// <summary>Rotated ellipse. <paramref name="shade"/> gets the old value and r² (0 at the center, 1 at the edge).</summary>
    public void Ellipse(float cx, float cy, float rx, float ry, float angle, Func<float, float, float> shade)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        int r = (int)MathF.Ceiling(MathF.Max(rx, ry));
        for (int y = (int)cy - r; y <= (int)cy + r; y++)
        for (int x = (int)cx - r; x <= (int)cx + r; x++)
        {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float a = (dx * c + dy * s) / rx, b = (-dx * s + dy * c) / ry;
            float r2 = a * a + b * b;
            if (r2 < 1) this[x, y] = shade(this[x, y], r2);
        }
    }

    /// <summary>Thin straight line (scratches).</summary>
    public void Line(float x0, float y0, float x1, float y1, Func<float, float> shade)
    {
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0))) + 1;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int x = (int)MathF.Round(x0 + (x1 - x0) * t), y = (int)MathF.Round(y0 + (y1 - y0) * t);
            this[x, y] = shade(this[x, y]);
        }
    }

    public unsafe void Save(string path)
    {
        var bytes = new byte[pixels.Length];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(Math.Clamp(pixels[i], 0f, 1f) * 255f + 0.5f);
        fixed (byte* data = bytes)
        {
            var image = new Image { Data = data, Width = Size, Height = Size, Mipmaps = 1, Format = PixelFormat.UncompressedGrayscale };
            if (!Raylib.ExportImage(image, path)) throw new IOException($"Could not write {path}");
        }
    }
}

/// <summary>Tileable noise. Every function repeats exactly over u, v in 0..1.</summary>
static class Noise
{
    /// <summary>Random value in 0..1 for an integer grid point.</summary>
    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>
    /// Smooth value noise with <paramref name="cellsX"/> by <paramref name="cellsY"/> cells across the image.
    /// Few cells along one axis and many along the other gives streaks (wood grain, brushed metal).
    /// </summary>
    public static float Value(float u, float v, int cellsX, int cellsY, int seed)
    {
        float x = u * cellsX, y = v * cellsY;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        int X(int i) => ((i % cellsX) + cellsX) % cellsX;
        int Y(int i) => ((i % cellsY) + cellsY) % cellsY;
        float a = Hash(X(x0), Y(y0), seed), b = Hash(X(x0 + 1), Y(y0), seed);
        float c = Hash(X(x0), Y(y0 + 1), seed), d = Hash(X(x0 + 1), Y(y0 + 1), seed);
        return float.Lerp(float.Lerp(a, b, tx), float.Lerp(c, d, tx), ty);
    }

    /// <summary>Fractal noise: <paramref name="octaves"/> layers of value noise, each twice as fine and half as strong.</summary>
    public static float Fbm(float u, float v, int cells, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value(u, v, cells << i, cells << i, seed + i * 101);
            norm += amp;
            amp *= 0.5f;
        }
        return sum / norm;
    }

    /// <summary>Ridged noise: close to 1 along thin wandering lines (cracks, creases).</summary>
    public static float Ridged(float u, float v, int cells, int octaves, int seed) =>
        1 - MathF.Abs(2 * Fbm(u, v, cells, octaves, seed) - 1);
}

/// <summary>One method per texture. Values are kept mostly between 0.6 and 1 so tinted colors stay close to the original.</summary>
static class Recipes
{
    static float Checker(float u, float v, float dark) => (u < 0.5f) ^ (v < 0.5f) ? dark : 1f;

    // Covers 8 m: the 2 x 2 checker gives the old 4 m floor tiles, which help you feel your speed.
    public static void Grass(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) =>
            (0.78f + 0.14f * Noise.Fbm(u, v, 4, 5, 1) + 0.06f * Noise.Value(u, v, 96, 24, 2) + 0.04f * Noise.Hash(x, y, 3))
            * Checker(u, v, 0.93f));
        for (int i = 0; i < 260; i++)
        {
            float bright = rng.Next(2) == 0 ? 0.86f : 1.07f;
            c.Ellipse(rng.NextSingle() * c.Size, rng.NextSingle() * c.Size, 2 + rng.NextSingle() * 3, 1.5f + rng.NextSingle() * 2,
                rng.NextSingle() * MathF.PI, (old, _) => old * bright);
        }
    }

    public static void Sand(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) =>
        {
            float ripple = MathF.Sin(MathF.Tau * (u * 5 + v * 3) + 2.5f * Noise.Fbm(u, v, 2, 2, 12));
            return (0.84f + 0.08f * Noise.Fbm(u, v, 4, 4, 11) + 0.05f * Noise.Hash(x, y, 13) + 0.025f * ripple) * Checker(u, v, 0.95f);
        });
        for (int i = 0; i < 140; i++)
        {
            float r = 1.2f + rng.NextSingle() * 2;
            c.Ellipse(rng.NextSingle() * c.Size, rng.NextSingle() * c.Size, r, r * 0.8f, rng.NextSingle() * MathF.PI, (old, r2) => old * (0.87f + 0.08f * r2));
        }
    }

    // Covers 4 m: poured panels with a seam every 2 m, stains and small pits.
    public static void Concrete(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float g = 0.8f + 0.12f * Noise.Fbm(u, v, 4, 5, 21) + 0.05f * Noise.Value(u, v, 6, 2, 22) + 0.04f * Noise.Hash(x, y, 23);
            if (Noise.Hash(x, y, 24) > 0.992f) g -= 0.15f;
            if (x % 128 < 2 || y % 128 < 2) g *= 0.72f;
            return g;
        });

    // Covers 4 m: smooth stucco with a faint course line every meter.
    public static void Plaster(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float g = 0.86f + 0.08f * Noise.Fbm(u, v, 8, 5, 31) + 0.04f * Noise.Value(u, v, 64, 64, 32) + 0.03f * Noise.Hash(x, y, 33);
            if (y % 64 < 2) g *= 0.86f;
            return g;
        });

    // Covers 2 m: 8 rows of 4 bricks, every other row shifted by half a brick.
    public static void Brick(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            int row = y / 32;
            int sx = (x + (row % 2) * 32) % c.Size;
            int lx = sx % 64, ly = y % 32;
            if (lx < 3 || ly < 3) return 0.95f + 0.03f * Noise.Hash(x, y, 40);
            float g = 0.72f + 0.14f * Noise.Hash(sx / 64, row, 41) + 0.08f * Noise.Fbm(u, v, 16, 3, 42) + 0.04f * Noise.Hash(x, y, 43);
            if (lx < 5 || ly < 5 || lx > 61 || ly > 29) g *= 0.93f;
            return g;
        });

    // Stretched once over each crate face: plank frame, five planks, a diagonal brace and nails.
    public static void Crate(Canvas c, Random rng)
    {
        const int border = 22;
        int s = c.Size;
        float plankH = (s - 2 * border) / 5f;
        c.Fill((u, v, x, y) =>
        {
            bool frame = x < border || y < border || x >= s - border || y >= s - border;
            if (frame)
            {
                bool vertical = (x < border || x >= s - border) && y >= border && y < s - border;
                float grain = vertical ? Noise.Value(u, v, 60, 3, 51) : Noise.Value(u, v, 3, 60, 51);
                bool innerEdge = x is >= border - 2 and < border || x >= s - border && x < s - border + 2
                              || y is >= border - 2 and < border || y >= s - border && y < s - border + 2;
                float g = 0.84f + 0.12f * grain + 0.04f * Noise.Hash(x, y, 52);
                return innerEdge ? g * 0.55f : g;
            }
            float d = (x + y - s) / MathF.Sqrt(2); // distance from the brace's center line
            if (MathF.Abs(d) < 18)
            {
                float along = (u - v) * 0.5f + 0.5f;
                float g = 0.86f + 0.12f * Noise.Value(along, d / 40f + 0.5f, 3, 30, 53);
                return MathF.Abs(d) > 16 ? g * 0.55f : g;
            }
            float py = (y - border) % plankH;
            if (py < 2) return 0.42f;
            int plank = (int)((y - border) / plankH);
            return 0.7f + 0.06f * Noise.Hash(plank, 0, 54) + 0.14f * Noise.Value(u, v, 3, 60, 55 + plank) + 0.04f * Noise.Hash(x, y, 56);
        });
        foreach (int cx in new[] { 11, s - 11 })
        foreach (int cy in new[] { 11, s - 11 })
            c.Ellipse(cx, cy, 3.5f, 3.5f, 0, (_, r2) => 0.35f + 0.4f * (1 - r2) * (1 - r2));
    }

    // Covers 2 m: diamond tread plate with a seam and corner bolts.
    public static void MetalPlate(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) =>
        {
            if (x < 2 || y < 2) return 0.55f;
            float g = 0.78f + 0.06f * Noise.Fbm(u, v, 8, 4, 61) + 0.03f * Noise.Hash(x, y, 62);
            int cellX = x / 32, cellY = y / 32;
            float dx = x % 32 - 15.5f, dy = y % 32 - 15.5f;
            bool flip = ((cellX + cellY) & 1) == 0;
            float a = (dx + (flip ? dy : -dy)) / MathF.Sqrt(2), b = (dx - (flip ? dy : -dy)) / MathF.Sqrt(2);
            float r2 = a * a / 144f + b * b / 12f;
            if (r2 < 1) g += 0.12f * MathF.Sqrt(1 - r2) + (b < 0 ? 0.04f : -0.03f);
            return g;
        });
        foreach (int cx in new[] { 9, 137 })
        foreach (int cy in new[] { 9, 137 })
            c.Ellipse(cx, cy, 4.5f, 4.5f, 0, (_, r2) => 0.6f + 0.35f * (1 - r2));
    }

    // Covers 2 m: mostly even paint, with chipped patches and scratches showing darker metal.
    public static void PaintedMetal(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) =>
        {
            float wear = Noise.Fbm(u, v, 8, 4, 71);
            return wear > 0.72f ? 0.7f + 0.1f * Noise.Hash(x, y, 72) : 0.92f + 0.04f * Noise.Fbm(u, v, 4, 3, 73);
        });
        for (int i = 0; i < 25; i++)
        {
            float x0 = rng.NextSingle() * c.Size, y0 = rng.NextSingle() * c.Size, len = 10 + rng.NextSingle() * 30, ang = rng.NextSingle() * MathF.Tau;
            c.Line(x0, y0, x0 + MathF.Cos(ang) * len, y0 + MathF.Sin(ang) * len, old => old * 0.78f);
        }
    }

    // Covers 3 m: lumpy stone with dark cracks and speckles.
    public static void Rock(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float g = 0.62f + 0.32f * Noise.Fbm(u, v, 3, 6, 81);
            float crack = Noise.Ridged(u, v, 4, 5, 82);
            if (crack > 0.96f) g *= 0.72f + (1 - crack) * 7; // thin cracks only
            float h = Noise.Hash(x, y, 83);
            return h > 0.97f ? g + 0.1f : h < 0.03f ? g - 0.1f : g;
        });

    // Covers 1 m: vertical ridges with deep fissures.
    public static void Bark(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float n = 0.6f * Noise.Value(u, v, 12, 2, 91) + 0.4f * Noise.Value(u, v, 24, 4, 92);
            float g = 0.5f + 0.45f * n;
            return Noise.Value(u, v, 10, 1, 93) < 0.35f ? g * 0.55f : g;
        });

    // Covers 2 m: hundreds of overlapping leaves over a dark base.
    public static void Leaves(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) => 0.42f + 0.1f * Noise.Fbm(u, v, 4, 3, 101));
        for (int i = 0; i < 1100; i++)
        {
            float bright = 0.65f + rng.NextSingle() * 0.35f;
            c.Ellipse(rng.NextSingle() * c.Size, rng.NextSingle() * c.Size, 5 + rng.NextSingle() * 5, 2.5f + rng.NextSingle() * 1.5f,
                rng.NextSingle() * MathF.PI, (_, r2) => bright * (0.85f + 0.15f * (1 - r2)));
        }
    }

    // Covers 1.2 m: two staggered rows of puffy bags with dark gaps between them.
    public static void Sandbag(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            int row = y / 64;
            int lx = (x + row * 32) % 64, ly = y % 64;
            float dx = (lx - 31.5f) / 30f, dy = (ly - 31.5f) / 27f;
            float r2 = dx * dx * dx * dx + dy * dy * dy * dy; // squircle: a bag shape
            if (r2 >= 1) return 0.35f;
            float g = 0.6f + 0.35f * MathF.Sqrt(1 - r2) + 0.04f * Noise.Fbm(u, v, 16, 3, 111) + ((x + y) % 2 == 0 ? 0.03f : -0.03f);
            return lx is >= 6 and < 8 ? g * 0.8f : g;
        });

    // Covers 0.5 m: three-tone camouflage on a twill weave. Tinted per bot.
    public static void Fabric(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float camo = Noise.Fbm(u, v, 3, 4, 121);
            float tone = camo < 0.44f ? 0.74f : camo < 0.56f ? 0.88f : 1f;
            float weave = (x + y) % 4 < 2 ? 1f : 0.93f;
            return tone * weave * (0.97f + 0.03f * Noise.Hash(x, y, 122));
        });

    // Covers 0.3 m: pebbled grain with creases. Boots, gloves, straps.
    public static void Leather(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) =>
        {
            float g = 0.72f + 0.12f * Noise.Fbm(u, v, 8, 4, 131) + 0.05f * Noise.Hash(x, y, 132);
            return Noise.Ridged(u, v, 6, 3, 133) > 0.93f ? g * 0.75f : g;
        });

    public static void Skin(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) => 0.93f + 0.05f * Noise.Fbm(u, v, 4, 4, 141) + 0.02f * Noise.Hash(x, y, 142));

    public static void Hair(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) => 0.55f + 0.35f * Noise.Value(u, v, 32, 2, 151) + 0.1f * Noise.Value(u, v, 64, 4, 152));

    // Covers 0.25 m: brushed steel with a few bright scratches.
    public static void GunMetal(Canvas c, Random rng)
    {
        c.Fill((u, v, x, y) => 0.82f + 0.08f * Noise.Value(u, v, 2, 128, 161) + 0.04f * Noise.Hash(x, y, 162));
        for (int i = 0; i < 12; i++)
        {
            float x0 = rng.NextSingle() * c.Size, y0 = rng.NextSingle() * c.Size, len = 8 + rng.NextSingle() * 24, ang = rng.NextSingle() * MathF.Tau;
            c.Line(x0, y0, x0 + MathF.Cos(ang) * len, y0 + MathF.Sin(ang) * len, _ => 0.97f);
        }
    }

    // Covers 0.2 m: fine stippled grip texture.
    public static void Polymer(Canvas c, Random rng) =>
        c.Fill((u, v, x, y) => 0.84f + 0.1f * Noise.Value(u, v, 48, 48, 171) + 0.04f * Noise.Hash(x, y, 172));
}
