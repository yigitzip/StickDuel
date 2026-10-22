using UnityEditor;
using UnityEngine;

namespace StickDuel.EditorTools
{
    /// <summary>
    /// Import settings for the runtime-loaded art under Assets/Resources (arenas, weapons):
    /// no mipmaps, never resized, clamped. Pivots and pixels-per-unit are set where the sprites
    /// are created in code.
    /// </summary>
    public sealed class RuntimeArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Arena/") && !assetPath.StartsWith("Assets/Resources/Weapons/"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            // The painted arena backgrounds are big; weapons stay crisp.
            importer.textureCompression = assetPath.Contains("/Bg_") ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.wrapMode = TextureWrapMode.Clamp;
        }
    }
}
