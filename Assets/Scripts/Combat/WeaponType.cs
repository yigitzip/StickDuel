namespace StickDuel
{
    /// <summary>The gameplay weapons a fighter can pick. Selection (menu, loadout, factory) goes through this.</summary>
    public enum WeaponType
    {
        Sword,
        Spear,
        Axe,
        Bow,
        Mace,
        SniperRifle,
    }

    /// <summary>Menu data per <see cref="WeaponType"/>. Only playable types can be selected.</summary>
    public static class WeaponTypeInfo
    {
        /// <summary>
        /// True when the type has a working gameplay behaviour (Sword, Spear). The others are
        /// skeletons (see <see cref="WeaponBehaviourBase"/>) and show as locked in the menu.
        /// </summary>
        public static bool IsPlayable(WeaponType type) => type == WeaponType.Sword || type == WeaponType.Spear;

        public static string DisplayName(WeaponType type) => type == WeaponType.SniperRifle ? "SNIPER RIFLE" : type.ToString().ToUpperInvariant();

        public static string Blurb(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Sword: return "Balanced and fast";
                case WeaponType.Spear: return "Two hands, long reach, the point kills";
                default: return "Coming soon";
            }
        }
    }
}
