using UnityEngine;

namespace StickDuel
{
    /// <summary>
    /// Legacy sprite/stat presets for <see cref="Sword"/>. Only <see cref="Katana"/> is used
    /// (by <see cref="SwordWeaponAdapter"/> and the castle sword sizing). The other entries in
    /// <see cref="All"/> (SAW, CLUB, SPEAR, STONE AXE) are old experiments, no longer offered in
    /// the menu and not the new gameplay weapons; selection now goes through
    /// <see cref="WeaponType"/>. Remove this file once the Sword reads its katana stats elsewhere.
    /// Art lives in Resources/Weapons (Tools/generate_weapons.py): pointing right, grip at x=48px.
    /// </summary>
    public sealed class Weapon
    {
        private const float GripPixel = 48f;
        private const float PixelsPerUnit = 213f; // same scale as the katana sprite

        public string Name;
        public string Blurb;
        /// <summary>Resources/Weapons texture; null = the katana already on the sword.</summary>
        public string Texture;
        public float TipPixel;
        public float BladeStartPixel;
        /// <summary>Hitbox thickness and vertical offset, in pixels (heads are not always centred).</summary>
        public float ThicknessPixels = 15f;
        public float CenterOffsetPixels;
        public float Damage = 1f;
        public float Knockback = 1f;
        public float HitCooldown = 1f;
        public float Recovery = 1f;

        private Sprite sprite;

        public float Tip => (TipPixel - GripPixel) / PixelsPerUnit;
        public float BladeStart => (BladeStartPixel - GripPixel) / PixelsPerUnit;
        public float Thickness => ThicknessPixels / PixelsPerUnit;
        public float CenterOffset => CenterOffsetPixels / PixelsPerUnit;

        public static readonly Weapon[] All =
        {
            new Weapon { Name = "KATANA", Blurb = "Balanced and fast", TipPixel = 314f, BladeStartPixel = 86f },
            new Weapon
            {
                Name = "SAW", Blurb = "Shreds with rapid hits", Texture = "Saw", TipPixel = 292f, BladeStartPixel = 84f,
                ThicknessPixels = 30f, Damage = 0.55f, Knockback = 0.5f, HitCooldown = 0.25f, Recovery = 0.2f,
            },
            new Weapon
            {
                Name = "CLUB", Blurb = "Sends them flying", Texture = "Club", TipPixel = 278f, BladeStartPixel = 150f,
                ThicknessPixels = 50f, Damage = 1.2f, Knockback = 2.2f, Recovery = 1.3f,
            },
            new Weapon
            {
                Name = "SPEAR", Blurb = "Longest reach", Texture = "Spear", TipPixel = 474f, BladeStartPixel = 260f,
                ThicknessPixels = 18f, Damage = 0.85f, Knockback = 1.1f,
            },
            new Weapon
            {
                Name = "STONE AXE", Blurb = "Crushing but slow", Texture = "StoneAxe", TipPixel = 276f, BladeStartPixel = 200f,
                ThicknessPixels = 66f, CenterOffsetPixels = 19f, Damage = 1.55f, Knockback = 1.5f, HitCooldown = 1.2f, Recovery = 1.7f,
            },
        };

        /// <summary>The katana preset the Sword is tuned for.</summary>
        public static Weapon Katana => All[0];

        /// <summary>The weapon's sprite (null for the katana, which keeps the sword's own).</summary>
        public Sprite Sprite
        {
            get
            {
                if (sprite == null && Texture != null)
                {
                    var texture = Resources.Load<Texture2D>("Weapons/" + Texture);
                    if (texture != null)
                    {
                        sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(GripPixel / texture.width, 0.5f), PixelsPerUnit);
                    }
                }

                return sprite;
            }
        }
    }
}
