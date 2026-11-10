using BigProject.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BigProject.EditorTools
{
    /// <summary>
    /// "Pose" dans la scène ouverte les éléments d'interface de combat que le HUD génère normalement au
    /// lancement (boutons, chrono, minimap...). Ils deviennent de vrais objets du Canvas : on peut les
    /// déplacer, les redimensionner et les restyler. Les éléments déjà présents ne sont pas touchés.
    /// </summary>
    public static class CombatHudInstaller
    {
        [MenuItem("Big Project/Ajouter l'interface de combat au Canvas")]
        private static void Install()
        {
            CombatHud hud = Object.FindAnyObjectByType<CombatHud>();
            if (hud == null)
            {
                Canvas canvas = Object.FindAnyObjectByType<Canvas>();
                if (canvas == null)
                {
                    EditorUtility.DisplayDialog(
                        "Interface de combat",
                        "Aucun Canvas dans la scène ouverte. Ouvre la scène de combat puis réessaie.",
                        "OK");
                    return;
                }

                hud = Undo.AddComponent<CombatHud>(canvas.gameObject);
            }

            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject, "Ajouter l'interface de combat");
            hud.BuildMissingElements();
            EditorUtility.SetDirty(hud);
            EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);

            EditorUtility.DisplayDialog(
                "Interface de combat",
                "Les éléments manquants ont été ajoutés au Canvas. Enregistre la scène (Ctrl+S) pour les conserver.",
                "OK");
        }
    }
}
