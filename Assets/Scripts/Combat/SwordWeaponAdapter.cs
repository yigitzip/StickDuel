using System;
using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// <see cref="IWeaponBehaviour"/> facade over the existing <see cref="Sword"/>. It only forwards
    /// calls; all blade behaviour (swing, parry, thrust, spin, hits, trail) stays in Sword.
    /// Fighter, the drivers and the rig still talk to <see cref="Fighter.sword"/> directly.
    /// </summary>
    [RequireComponent(typeof(Sword))]
    public sealed class SwordWeaponAdapter : MonoBehaviour, IWeaponBehaviour
    {
        private Sword sword;

        public Sword Sword => sword != null ? sword : sword = GetComponent<Sword>();
        public WeaponType Type => WeaponType.Sword;
        public Fighter Owner => IsEquipped ? Sword.Owner : null;
        public bool IsEquipped { get; private set; }
        public bool IsAttacking => Sword.IsFlying || (Sword.IsHeld && (Sword.IsThrusting || Sword.SpinProgress >= 0f));

        /// <summary>Same as the old direct call: katana stats via <see cref="Sword.Equip(Weapon)"/>.</summary>
        public void Equip(Fighter owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Sword.Owner = owner;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Sword.Equip(Weapon.Katana);
            IsEquipped = true;
        }

        /// <summary>Hides the blade. Not called in gameplay yet (nothing swaps weapons mid-match).</summary>
        public void Unequip()
        {
            IsEquipped = false;
            gameObject.SetActive(false);
        }
    }
}
