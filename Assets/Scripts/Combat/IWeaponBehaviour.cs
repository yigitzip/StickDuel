namespace StickDuel
{
    /// <summary>
    /// Lifecycle shared by every gameplay weapon. Deliberately small: weapon specific moves (the
    /// sword's parry/thrust/spin, a bow's projectiles, a rifle's shot) stay on the concrete class.
    /// </summary>
    public interface IWeaponBehaviour
    {
        WeaponType Type { get; }

        /// <summary>The fighter holding it, null while unequipped.</summary>
        Fighter Owner { get; }

        bool IsEquipped { get; }

        /// <summary>True while an attack of this weapon is in progress.</summary>
        bool IsAttacking { get; }

        /// <summary>Hands the weapon to <paramref name="owner"/>. Throws on null.</summary>
        void Equip(Fighter owner);

        void Unequip();
    }
}
