using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>
/// Gunshot sounds. Each file is loaded once, then played through a small pool of
/// "voices" (sound aliases that share the same sample data). A raylib Sound can
/// only play one copy at a time, and calling PlaySound again restarts it, so a
/// 10-shots-per-second rifle needs several voices to let shots overlap.
/// Missing files or a missing audio device just mean silence, never a crash.
/// </summary>
public sealed class Audio : IDisposable
{
    const int VoicesPerSound = 16;
    const float SilenceThreshold = 0.01f;   // about -40 dB
    const float HearingRange = 70f;         // meters; farther shots are silent
    const float PlayerVolume = 0.55f, OtherVolume = 0.45f;

    sealed class Bank(Sound source, Sound[] voices)
    {
        public readonly Sound Source = source;
        public readonly Sound[] Voices = voices;
        public int Next;
    }

    readonly Dictionary<string, Bank> banks = [];

    /// <summary>Opens the audio device and loads every weapon's sound from <paramref name="folder"/>.</summary>
    public Audio(string folder, IEnumerable<string> files)
    {
        Raylib.InitAudioDevice();
        if (!Raylib.IsAudioDeviceReady()) return;
        foreach (var file in files.Distinct())
        {
            string path = Path.Combine(folder, file);
            if (!File.Exists(path)) continue;
            var wave = LoadTrimmed(path);
            var source = Raylib.LoadSoundFromWave(wave);
            Raylib.UnloadWave(wave);
            if (!Raylib.IsSoundValid(source)) continue;
            var voices = new Sound[VoicesPerSound];
            for (int i = 0; i < voices.Length; i++) voices[i] = Raylib.LoadSoundAlias(source);
            banks[file] = new Bank(source, voices);
        }
    }

    /// <summary>Your own gun: full volume, centered.</summary>
    public void PlayOwnShot(string file) => Play(file, PlayerVolume, 0f);

    /// <summary>
    /// Someone else's gun, heard from <paramref name="listener"/>: quieter with distance
    /// (squared falloff sounds natural) and panned toward the side it came from.
    /// </summary>
    public void PlayShotAt(string file, Vector3 source, Vector3 listener, Vector3 listenerForward)
    {
        var toSource = source - listener;
        float dist = toSource.Length();
        if (dist >= HearingRange) return;
        float falloff = 1f - dist / HearingRange;
        // Right = Cross(forward, up), same as everywhere else in the game; dot gives -1 (left) .. 1 (right).
        var right = Vector3.Normalize(Vector3.Cross(listenerForward, Vector3.UnitY));
        float pan = dist > 0.01f ? Vector3.Dot(toSource / dist, right) : 0f;
        Play(file, OtherVolume * falloff * falloff, pan * 0.8f);
    }

    void Play(string file, float volume, float pan)
    {
        if (!banks.TryGetValue(file, out var bank)) return;
        // Round-robin: reuse the oldest voice. By the time it comes around again it has
        // usually finished, so nothing audible gets cut off.
        var voice = bank.Voices[bank.Next];
        bank.Next = (bank.Next + 1) % bank.Voices.Length;
        Raylib.SetSoundVolume(voice, volume);
        Raylib.SetSoundPan(voice, Math.Clamp(pan, -1f, 1f));
        Raylib.SetSoundPitch(voice, MathUtil.Range(0.94f, 1.06f)); // tiny variation so repeats don't sound robotic
        Raylib.PlaySound(voice);
    }

    /// <summary>
    /// Loads a file and cuts silence from both ends. Leading silence would make the
    /// shot sound late; a long silent tail would keep a voice busy for nothing.
    /// </summary>
    static unsafe Wave LoadTrimmed(string path)
    {
        var wave = Raylib.LoadWave(path);
        int frames = (int)wave.FrameCount, channels = (int)wave.Channels;
        if (frames == 0 || channels == 0) return wave;

        float* samples = Raylib.LoadWaveSamples(wave); // interleaved, -1..1
        int first = 0, last = frames - 1;
        while (first < frames && !IsLoud(samples, first, channels)) first++;
        while (last > first && !IsLoud(samples, last, channels)) last--;
        Raylib.UnloadWaveSamples(samples);

        if (first >= last) return wave; // all silent: leave it alone
        int tail = (int)wave.SampleRate / 20; // keep 50 ms after the last loud sample so the fade isn't chopped
        Raylib.WaveCrop(ref wave, first, Math.Min(frames, last + 1 + tail));
        return wave;
    }

    static unsafe bool IsLoud(float* samples, int frame, int channels)
    {
        for (int c = 0; c < channels; c++)
            if (MathF.Abs(samples[frame * channels + c]) > SilenceThreshold) return true;
        return false;
    }

    public void Dispose()
    {
        foreach (var bank in banks.Values)
        {
            foreach (var v in bank.Voices) Raylib.UnloadSoundAlias(v);
            Raylib.UnloadSound(bank.Source);
        }
        banks.Clear();
        Raylib.CloseAudioDevice();
    }
}
