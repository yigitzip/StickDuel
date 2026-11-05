using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Prepares the <see cref="IWeaponBehaviour"/> for a <see cref="WeaponType"/> on a fighter
    /// (get-or-add, so calling it again is safe): the sword adapter (katana or laser sword preset) on the fighter's Sword, a new
    /// weapon object for the spear, axe, mace, bow and sniper rifle. It does not equip; the caller does.
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
                case WeaponType.LaserSword:
                    if (owner.sword == null)
                    {
                        Debug.LogError($"WeaponFactory: {owner.name} has no Sword rig for {type}.", owner);
                        return null;
                    }

                    GameObject rig = owner.sword.gameObject;
                    return type == WeaponType.Sword ? GetOrAdd<SwordWeaponAdapter>(rig) : GetOrAdd<LaserSwordWeapon>(rig);
                case WeaponType.Spear: return owner.heldWeapon is SpearWeapon spear ? spear : SpearWeapon.Create(owner);
                case WeaponType.Axe: return owner.heldWeapon is AxeWeapon axe ? axe : AxeWeapon.Create(owner);
                case WeaponType.Bow: return owner.heldWeapon is BowWeapon bow ? bow : BowWeapon.Create(owner);
                case WeaponType.Mace: return owner.heldWeapon is MaceWeapon mace ? mace : MaceWeapon.Create(owner);
                case WeaponType.SniperRifle: return owner.heldWeapon is SniperRifleWeapon rifle ? rifle : SniperRifleWeapon.Create(owner);
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
