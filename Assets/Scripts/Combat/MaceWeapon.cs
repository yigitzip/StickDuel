using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Flanged mace: <see cref="HeavyWeapon"/> mechanics with the head centred on the handle, so it
    /// hits with any side and never needs mirroring.
    /// Art: Resources/Weapons/Mace.png, 400 px/unit, 1.7 units long, rear hand at x=140px.
    /// </summary>
    public sealed class MaceWeapon : HeavyWeapon
    {
        private const float GripPixel = 140f;
        private const float Butt = -0.345f;
        private const float HeadStart = 0.92f;
        private const float Tip = 1.355f;

        private static Sprite art;

        public override WeaponType Type => WeaponType.Mace;

        /// <summary>The mace art (also the menu card). Null, with an error, if the texture is missing.</summary>
        public static Sprite Art => art != null ? art : art = LoadArt("Mace", PixelsPerUnit, GripPixel);

        /// <summary>Builds the mace for <paramref name="owner"/> (unequipped). Null, with an error, if the art is missing.</summary>
        public static MaceWeapon Create(Fighter owner) => Build<MaceWeapon>(owner, "Mace", Art, 2.2f, Butt, HeadStart, 0.07f,
            new Vector2((HeadStart + Tip) * 0.5f, 0f), new Vector2(Tip - HeadStart, 0.32f), new Vector2(Tip - 0.08f, 0f));
    }
}
