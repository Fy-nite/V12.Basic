using System;
using V12.Core.Core.Interfaces;
using V12.Core;
using V12.Components;

namespace V12.Basic.Components
{
    /// <summary>
    /// Simple health/damage component.
    /// Systems can read CurrentHealth to determine alive/dead state.
    /// IsInvincible prevents damage from being applied.
    /// </summary>
    [ComponentEntry("GAMEPLAY", "Health", SortOrder = 8, Icon = "heart")]
    public class HealthComponent : ComponentBase
    {
        private float _currentHealth;
        private float _maxHealth;
        private bool  _isInvincible = false;

        public override string Name        => "Health";
        public override string Description => "Health points";

        public float MaxHealth
        {
            get => _maxHealth;
            set => SetSync(ref _maxHealth, MathF.Max(0f, value), 0.001f);
        }
        public float CurrentHealth
        {
            get => _currentHealth;
            set => SetSync(ref _currentHealth, Math.Clamp(value, 0f, _maxHealth), 0.001f);
        }
        public bool IsInvincible
        {
            get => _isInvincible;
            set => SetSync(ref _isInvincible, value);
        }
        public bool IsAlive => _currentHealth > 0f;

        public HealthComponent() : this(100f) { }
        public HealthComponent(float maxHealth, bool invincible = false)
        {
            _maxHealth = MathF.Max(0f, maxHealth);
            _currentHealth = _maxHealth;
            _isInvincible = invincible;
        }

        /// <summary>Applies damage, respecting invincibility. Returns actual damage dealt.</summary>
        public float TakeDamage(float amount)
        {
            if (_isInvincible || amount <= 0f) return 0f;
            var actual = MathF.Min(amount, _currentHealth);
            CurrentHealth -= actual;
            return actual;
        }

        /// <summary>Restores health up to MaxHealth. Returns actual amount healed.</summary>
        public float Heal(float amount)
        {
            if (amount <= 0f) return 0f;
            var actual = MathF.Min(amount, _maxHealth - _currentHealth);
            CurrentHealth += actual;
            return actual;
        }
        public override IWorldElement BuildUI()
        {
            return new Element();
        }
        public override string ToString() =>
            $"Health({CurrentHealth:F1}/{MaxHealth:F1} Invincible:{IsInvincible})";
    }
}
