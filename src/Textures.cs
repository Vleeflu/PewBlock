using Raylib_cs;

namespace PewBlock;

/// <summary>What a surface is made of. Picks the texture drawn on it.</summary>
public enum Surface
{
    None, // plain color
    Grass, Sand, Concrete, Plaster, Brick, Crate, MetalPlate, PaintedMetal, Rock, Bark, Leaves, Sandbag,
    Fabric, Leather, Skin, Hair, GunMetal, Polymer,
}

/// <summary>
/// The PNG textures in assets/textures (made by tools/AssetGen). They are grayscale
/// "detail maps": the GPU multiplies each pixel by the shape's color, so one texture
/// works for every color. A missing file just means that surface is drawn plain.
/// </summary>
public static class Textures
{
    /// <summary>
    /// File name and how many meters one copy of the image covers before it repeats.
    /// 0 means "stretch once over each face" (a crate shows exactly one crate side).
    /// </summary>
    static readonly Dictionary<Surface, (string File, float Meters)> Files = new()
    {
        [Surface.Grass] = ("floor_grass", 8),
        [Surface.Sand] = ("floor_sand", 8),
        [Surface.Concrete] = ("concrete", 4),
        [Surface.Plaster] = ("plaster", 4),
        [Surface.Brick] = ("brick", 2),
        [Surface.Crate] = ("crate", 0),
        [Surface.MetalPlate] = ("metal_plate", 2),
        [Surface.PaintedMetal] = ("painted_metal", 2),
        [Surface.Rock] = ("rock", 3),
        [Surface.Bark] = ("bark", 1),
        [Surface.Leaves] = ("leaves", 2),
        [Surface.Sandbag] = ("sandbag", 1.2f),
        [Surface.Fabric] = ("fabric", 0.5f),
        [Surface.Leather] = ("leather", 0.3f),
        [Surface.Skin] = ("skin", 0.5f),
        [Surface.Hair] = ("hair", 0.2f),
        [Surface.GunMetal] = ("gun_metal", 0.25f),
        [Surface.Polymer] = ("polymer", 0.2f),
    };

    static readonly Texture2D?[] loaded = new Texture2D?[Enum.GetValues<Surface>().Length];

    /// <summary>Loads every texture from <paramref name="folder"/>. Needs the window (OpenGL) to exist.</summary>
    public static void Load(string folder)
    {
        foreach (var (surface, (file, _)) in Files)
        {
            string path = Path.Combine(folder, file + ".png");
            if (!File.Exists(path)) continue;
            var texture = Raylib.LoadTexture(path);
            if (!Raylib.IsTextureValid(texture)) continue;
            // Mipmaps (pre-shrunk copies) + trilinear filtering stop far-away floors from shimmering;
            // anisotropic filtering keeps them sharp at grazing angles.
            Raylib.GenTextureMipmaps(ref texture);
            Raylib.SetTextureFilter(texture, TextureFilter.Anisotropic8X);
            Raylib.SetTextureFilter(texture, TextureFilter.Trilinear);
            loaded[(int)surface] = texture;
        }
    }

    public static void Unload()
    {
        for (int i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] is { } texture) Raylib.UnloadTexture(texture);
            loaded[i] = null;
        }
    }

    /// <summary>OpenGL texture id; raylib's 1x1 white texture for plain surfaces.</summary>
    public static uint Id(Surface surface) => loaded[(int)surface]?.Id ?? Rlgl.GetTextureIdDefault();

    /// <summary>Meters per repeat (0 = stretch once per face).</summary>
    public static float Meters(Surface surface) => Files.TryGetValue(surface, out var f) ? f.Meters : 1;
}
