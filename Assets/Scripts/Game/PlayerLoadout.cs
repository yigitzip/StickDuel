using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>The player's weapon choice, by <see cref="WeaponType"/>. Only playable types are accepted.</summary>
    public sealed class PlayerLoadout
    {
        public WeaponType Weapon { get; private set; } = WeaponType.Sword;

        /// <summary>Logs an error and keeps the current choice for unknown or locked types.</summary>
        public bool TrySelect(WeaponType type)
        {
            if (!Enum.IsDefined(typeof(WeaponType), type))
            {
                Debug.LogError($"PlayerLoadout: unknown weapon type {(int)type}, keeping {Weapon}.");
                return false;
            }

            if (!WeaponTypeInfo.IsPlayable(type))
            {
                Debug.LogError($"PlayerLoadout: {type} is not playable yet, keeping {Weapon}.");
                return false;
            }

            Weapon = type;
            return true;
        }
    }
}
