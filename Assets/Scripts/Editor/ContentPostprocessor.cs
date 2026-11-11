using BigProject.Client;
using UnityEditor;
using UnityEngine;

namespace BigProject.EditorTools
{
    /// <summary>
    /// Prévient quand un sort ou un personnage est créé hors de Resources/Spells ou Resources/Characters :
    /// le jeu ne le chargerait pas, et on se demanderait pourquoi il n'apparaît pas.
    /// </summary>
    public sealed class ContentPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!path.EndsWith(".asset") || path.StartsWith("Assets/Resources/Spells/") ||
                    path.StartsWith("Assets/Resources/Characters/"))
                {
                    continue;
                }

                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset is SpellAsset)
                {
                    Debug.LogWarning("[Contenu] Ce sort n'est pas dans Assets/Resources/Spells : il ne sera pas chargé. " + path, asset);
                }
                else if (asset is CharacterAsset)
                {
                    Debug.LogWarning("[Contenu] Ce personnage n'est pas dans Assets/Resources/Characters : il ne sera pas chargé. " + path, asset);
                }
            }
        }
    }
}
