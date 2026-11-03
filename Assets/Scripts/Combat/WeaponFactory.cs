using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Prepares the <see cref="IWeaponBehaviour"/> for a <see cref="WeaponType"/> on a fighter
    /// (get-or-add, so calling it again is safe): the sword adapter on the fighter's Sword, a new
    /// spear object for the spear, skeleton components for the rest. It does not equip; the caller does.
    /// </summary>
    public static class WeaponFactory
    {
        /// <summary>Returns null and logs an error when the fighter can't carry that weapon.</summary>
        public static IWeaponBehaviour Prepare(WeaponType type, Fighter owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            switch (type)
            {
                case WeaponType.Sword:
                    if (owner.sword == null)
                    {
                        Debug.LogError($"WeaponFactory: {owner.name} has no Sword to adapt.", owner);
                        return null;
                    }

                    return GetOrAdd<SwordWeaponAdapter>(owner.sword.gameObject);
                case WeaponType.Spear: return owner.spear != null ? owner.spear : SpearWeapon.Create(owner);
                case WeaponType.Axe: return GetOrAdd<AxeWeapon>(owner.gameObject);
                case WeaponType.Bow: return GetOrAdd<BowWeapon>(owner.gameObject);
                case WeaponType.Mace: return GetOrAdd<MaceWeapon>(owner.gameObject);
                case WeaponType.SniperRifle: return GetOrAdd<SniperRifleWeapon>(owner.gameObject);
                default:
                    Debug.LogError($"WeaponFactory: unknown weapon type {(int)type}.", owner);
                    return null;
            }
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
        }
    }
}
