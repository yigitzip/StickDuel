using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Battle axe: <see cref="HeavyWeapon"/> mechanics with the blade below the handle (mirrored
    /// when the fighter turns round so the edge always leads).
    /// Art: Resources/Weapons/BattleAxe.png, 400 px/unit, 1.7 units long, rear hand at x=140px.
    /// </summary>
    public sealed class AxeWeapon : HeavyWeapon
    {
        private const float GripPixel = 140f;
        private const float Butt = -0.345f;
        private const float HandleEnd = 1.325f;

        private static Sprite art;

        public override WeaponType Type => WeaponType.Axe;

        /// <summary>The axe art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("BattleAxe", PixelsPerUnit, GripPixel);

        /// <summary>Builds the axe for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static AxeWeapon Create(Fighter owner) => Build<AxeWeapon>(owner, "Battle Axe", Art, 2f, Butt, HandleEnd, 0.07f,
            new Vector2(1.17f, -0.15f), new Vector2(0.37f, 0.3f), new Vector2(1.26f, -0.24f));
    }
}
