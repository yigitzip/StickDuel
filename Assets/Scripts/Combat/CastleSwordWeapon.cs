using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// A medieval arming sword on the katana's physics (<see cref="SwordRigWeapon"/>): the katana's
    /// moves, reach and stats, a knight's blade for art. Picked from the menu like any weapon; the
    /// castle guards carry it too (<see cref="StormTheCastleMode"/>).
    /// Art: Resources/Castle/Sword, pointing right, grip centre at x=80px on the blade axis (mid
    /// height), tip at x=503px, scaled so the tip lands where the katana's does.
    /// </summary>
    public sealed class CastleSwordWeapon : SwordRigWeapon
    {
        private static Weapon preset;
        private static Sprite art;

        public static Weapon ArmingSword => preset != null ? preset : preset = new Weapon
        {
            Name = "CASTLE SWORD", TipPixel = Weapon.Katana.TipPixel, BladeStartPixel = Weapon.Katana.BladeStartPixel, Sprite = Art,
        };

        /// <summary>The blade sprite, grip at the pivot. Null, with an error, if the texture is missing.</summary>
        public static Sprite Art
        {
            get
            {
                if (art != null) return art;
                const float gripPixel = 80f, tipPixel = 503f;
                var texture = Resources.Load<Texture2D>("Castle/Sword");
                if (texture == null)
                {
                    Debug.LogError("CastleSwordWeapon: Resources/Castle/Sword.png is missing.");
                    return null;
                }

                return art = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(gripPixel / texture.width, 0.5f), (tipPixel - gripPixel) / Weapon.Katana.Tip);
            }
        }

        public override WeaponType Type => WeaponType.CastleSword;
        protected override Weapon Preset => ArmingSword;
    }
}
