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
        CastleSword,
    }

    /// <summary>Menu data per <see cref="WeaponType"/>. Only playable types can be selected.</summary>
    public static class WeaponTypeInfo
    {
        /// <summary>
        /// True when the type has a working gameplay behaviour (all of them now). A future skeleton
        /// (see <see cref="WeaponBehaviourBase"/>) returns false here and shows as locked in the menu.
        /// </summary>
        public static bool IsPlayable(WeaponType type) => true;

        public static string DisplayName(WeaponType type)
        {
            switch (type)
            {
                case WeaponType.SniperRifle: return "SNIPER RIFLE";
                case WeaponType.LaserSword: return "LASER SWORD";
                case WeaponType.CastleSword: return "CASTLE SWORD";
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
                case WeaponType.CastleSword: return "Katana moves, a knight's blade";
                case WeaponType.Bow: return "Hold LEFT CLICK to draw, release to shoot";
                case WeaponType.SniperRifle: return "LEFT CLICK fires along the laser";
                default: return "Coming soon";
            }
        }
    }
}
