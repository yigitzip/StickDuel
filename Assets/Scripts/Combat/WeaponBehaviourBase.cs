using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Shared skeleton for the weapons that are not implemented yet (SniperRifle). It only tracks ownership: no attacks, damage, hitboxes or input. These are
    /// not wired into gameplay; <see cref="WeaponTypeInfo.IsPlayable"/> keeps them out of the menu.
    /// Each weapon grows its own mechanics in its subclass, not in this base or the interface.
    /// </summary>
    public abstract class WeaponBehaviourBase : MonoBehaviour, IWeaponBehaviour
    {
        public abstract WeaponType Type { get; }
        public Fighter Owner { get; private set; }
        public bool IsEquipped => Owner != null;
        public virtual bool IsAttacking => false;

        public virtual void Equip(Fighter owner)
        {
            Owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
        }

        public virtual void Unequip()
        {
            Owner = null;
        }
    }
}
