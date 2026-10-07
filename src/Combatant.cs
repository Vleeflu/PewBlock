using System.Numerics;

namespace PewBlock;

/// <summary>
/// Anyone who can shoot and be shot: the player and every bot. Bots pick targets
/// from a list of these, so they treat the player and each other the same way.
/// </summary>
public interface ICombatant
{
    string Name { get; }
    bool Alive { get; }
    float Health { get; }
    int Kills { get; set; }
    int Deaths { get; set; }
    /// <summary>Counts down while eliminated; respawn at zero.</summary>
    float RespawnTimer { get; set; }
    /// <summary>Battle royale only: 0 while alive, finishing position (1 = winner) once eliminated.</summary>
    int Placement { get; set; }
    Vector3 Feet { get; }
    Vector3 Eye { get; }
    float HorizontalSpeed { get; }
    bool Grounded { get; }
    /// <summary>Returns true if this damage eliminated the combatant.</summary>
    bool TakeDamage(float amount);
}

/// <summary>Health and respawn rules shared by everyone.</summary>
public static class Combat
{
    public const float MaxHealth = 100f;
    public const float RegenDelay = 4f;     // seconds without damage before healing starts
    public const float RegenRate = 25f;     // health per second
    public const float RespawnDelay = 2.5f; // seconds dead before respawning
}
