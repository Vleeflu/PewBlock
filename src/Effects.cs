using System.Numerics;
using Raylib_cs;

namespace PewBlock;

/// <summary>Short-lived visuals: bullet tracers and particle bursts.</summary>
public sealed class Effects
{
    struct Tracer
    {
        public Vector3 From, To;
        public float Life;
        public Color Color;
    }

    struct Particle
    {
        public Vector3 Position, Velocity;
        public float Life, MaxLife, Size;
        public Color Color;
    }

    const float TracerLife = 0.08f;
    readonly List<Tracer> tracers = [];
    readonly List<Particle> particles = [];

    public void AddTracer(Vector3 from, Vector3 to, Color color) =>
        tracers.Add(new Tracer { From = from, To = to, Life = TracerLife, Color = color });

    public void Burst(Vector3 at, Color color, int count, float speed = 4f)
    {
        for (int i = 0; i < count; i++)
        {
            float life = MathUtil.Range(0.35f, 0.7f);
            particles.Add(new Particle
            {
                Position = at,
                Velocity = MathUtil.RandomInSphere() * speed + new Vector3(0, 1.5f, 0),
                Life = life,
                MaxLife = life,
                Size = MathUtil.Range(0.06f, 0.14f),
                Color = color,
            });
        }
    }

    public void Update(float dt)
    {
        for (int i = 0; i < tracers.Count; i++)
        {
            var t = tracers[i];
            t.Life -= dt;
            tracers[i] = t;
        }
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            p.Life -= dt;
            p.Velocity.Y -= 18f * dt;
            p.Position += p.Velocity * dt;
            if (p.Position.Y < 0) { p.Position.Y = 0; p.Velocity *= 0.4f; }
            particles[i] = p;
        }
        tracers.RemoveAll(t => t.Life <= 0);
        particles.RemoveAll(p => p.Life <= 0);
    }

    public void Draw()
    {
        foreach (var t in tracers)
            Raylib.DrawCylinderEx(t.From, t.To, 0.012f, 0.012f, 4, Raylib.ColorAlpha(t.Color, t.Life / TracerLife));
        foreach (var p in particles)
            Raylib.DrawCube(p.Position, p.Size, p.Size, p.Size, Raylib.ColorAlpha(p.Color, p.Life / p.MaxLife));
    }

    public void Clear()
    {
        tracers.Clear();
        particles.Clear();
    }
}
