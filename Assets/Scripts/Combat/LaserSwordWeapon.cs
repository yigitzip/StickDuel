namespace StickDuel
{
    /// <summary>
    /// Energy blade on the katana's physics (<see cref="SwordRigWeapon"/>): same controls, a touch
    /// longer, a thin cutting core that hurts more but pushes less (it burns rather than batters).
    /// Art: Resources/Weapons/LaserSword.png at katana scale (213 px/unit, grip x=48px), blade from x=76px, tip x=325px.
    /// </summary>
    public sealed class LaserSwordWeapon : SwordRigWeapon
    {
        public static readonly Weapon LaserBlade = new Weapon
        {
            Name = "LASER SWORD", Texture = "LaserSword", TipPixel = 325f, BladeStartPixel = 76f,
            ThicknessPixels = 24f, Damage = 1.2f, Knockback = 0.75f, HitCooldown = 0.9f, Recovery = 0.9f,
        };

        public override WeaponType Type => WeaponType.LaserSword;
        protected override Weapon Preset => LaserBlade;
    }
}
