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
        LaserSword,
    }

    /// <summary>Menu data per <see cref="WeaponType"/>. Only playable types can be selected.</summary>
    public static class WeaponTypeInfo
    {
        /// <summary>
        /// True when the type has a working gameplay behaviour (all but SniperRifle). The rest are
        /// skeletons (see <see cref="WeaponBehaviourBase"/>) and show as locked in the menu.
        /// </summary>
        public static bool IsPlayable(WeaponType type) => type != WeaponType.SniperRifle;

        public static string DisplayName(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.SniperRifle: return "SNIPER RIFLE";
                case WeaponType.LaserSword: return "LASER SWORD";
                default: return type.ToString().ToUpperInvariant();
            }
        }

        public static string Blurb(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.Sword: return "Balanced and fast";
                case WeaponType.Spear: return "Long reach, only the point cuts";
                case WeaponType.Axe: return "Heavy chops, SHIFT slam, F leap";
                case WeaponType.Mace: return "Crushing blows, SHIFT slam, F leap";
                case WeaponType.LaserSword: return "Katana moves, burns deeper";
                case WeaponType.Bow: return "Hold LEFT CLICK to draw, release to shoot";
                default: return "Coming soon";
            }
        }
    }
}
