using UnityEngine;

namespace StickDuel
{
    /// <summary>Enemy bots for the run modes: clones of the scene's (deactivated) enemy fighter.</summary>
    public static class Bots
    {
        /// <summary>
        /// A fresh, still inactive clone of <paramref name="template"/> with its own blade, wired to
        /// fight <paramref name="opponent"/> and flagged as a guard (guards never hurt each other).
        /// The caller sets health, damage and skill, then activates it and the blade.
        /// </summary>
        public static Fighter Clone(Fighter template, Fighter opponent)
        {
            // The template is inactive, so the clones are too: wire them up before their Awake runs.
            Fighter bot = Object.Instantiate(template);
            Sword sword = Object.Instantiate(template.sword);
            // Rig and blade trail build their pieces as children at Awake; drop the template's copies.
            KeepOnly(bot.transform, template.headCollider.name);
            KeepOnly(sword.transform, "Edge", "Tip");
            bot.sword = sword;
            sword.Owner = bot;
            bot.isGuard = true;
            bot.opponent = opponent;
            return bot;
        }

        private static void KeepOnly(Transform parent, params string[] names)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                if (System.Array.IndexOf(names, child.name) < 0)
                {
                    Object.DestroyImmediate(child);
                }
            }
        }
    }
}
