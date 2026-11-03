using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// <see cref="IWeaponBehaviour"/> facade for weapons that ride on the existing <see cref="Sword"/>
    /// (swing, parry, thrust, spin, hits, trail all stay in Sword). A subclass only picks the
    /// <see cref="Weapon"/> preset: sprite, reach, hitbox and stats, applied with
    /// <see cref="Sword.Equip(Weapon)"/>. Fighter, the drivers and the rig still talk to
    /// <see cref="Fighter.sword"/> directly.
    /// </summary>
    [RequireComponent(typeof(Sword))]
    public abstract class SwordRigWeapon : MonoBehaviour, IWeaponBehaviour
    {
        private Sword sword;

        public Sword Sword => sword != null ? sword : sword = GetComponent<Sword>();
        public abstract WeaponType Type { get; }
        /// <summary>Applied to the Sword on <see cref="Equip"/>.</summary>
        protected abstract Weapon Preset { get; }
        public Fighter Owner => IsEquipped ? Sword.Owner : null;
        public bool IsEquipped { get; private set; }
        public bool IsAttacking => Sword.IsFlying || (Sword.IsHeld && (Sword.IsThrusting || Sword.SpinProgress >= 0f));

        public void Equip(Fighter owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Sword.Owner = owner;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Sword.Equip(Preset);
            IsEquipped = true;
        }

        /// <summary>Hides the blade. Not called in gameplay yet (nothing swaps weapons mid-match).</summary>
        public void Unequip()
        {
            IsEquipped = false;
            gameObject.SetActive(false);
        }
    }

    /// <summary>The katana, exactly as before: <see cref="Sword.Equip(Weapon)"/> with <see cref="Weapon.Katana"/>.</summary>
    public sealed class SwordWeaponAdapter : SwordRigWeapon
    {
        public override WeaponType Type => WeaponType.Sword;
        protected override Weapon Preset => Weapon.Katana;
    }
}
