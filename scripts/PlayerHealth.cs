using System;
using Godot;

/// res://scripts/PlayerHealth.cs
/// Tracks player HP and delivers TakeDamage / Die.
/// Stub: prints to console now; RPC-ready for multiplayer phase.
public partial class PlayerHealth : Node
{
    public const float MaxHealth = 100f;

    float _hp   = MaxHealth;
    bool  _dead = false;

    public float CurrentHealth => _hp;

    /// Fired when damage is dealt — used by OnlineDungeonArena to relay damage to the peer.
    public event Action<float>? DamageDealt;

    /// Fired once when HP reaches zero.
    public event Action? OnDied;

    public void TakeDamage(float amount, Vector3 hitWorldPos)
    {
        if (_dead) return;
        _hp = Mathf.Max(_hp - amount, 0f);
        GD.Print($"[Health] {GetParent().Name}: {_hp:F0}/{MaxHealth} HP  (-{amount:F0})");
        DamageDealt?.Invoke(amount);
        if (_hp <= 0f) Die();
    }

    public void Die()
    {
        if (_dead) return;
        _dead = true;
        GD.Print($"[Health] {GetParent().Name} died!");
        OnDied?.Invoke();
    }

    public void Heal(float amount)
    {
        _hp = Mathf.Min(_hp + amount, MaxHealth);
    }
}
