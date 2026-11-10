using System.IO;
using BigProject.Client;
using BigProject.Core.Match;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigProject.EditorTools
{
    /// <summary>
    /// Copie la Tilemap de la scène de combat dans Assets/Resources/Maps/CombatMap.asset pour que le serveur
    /// (qui ne charge pas cette scène) joue sur la même carte. Exécuté à chaque enregistrement d'une scène
    /// qui contient un MapSource, ou à la demande depuis le menu.
    /// </summary>
    [InitializeOnLoad]
    public static class MapBaker
    {
        private const string AssetPath = "Assets/Resources/Maps/CombatMap.asset";

        static MapBaker()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        [MenuItem("Big Project/Générer la carte du serveur")]
        private static void BakeFromMenu()
        {
            Bake(SceneManager.GetActiveScene(), out string message);
            EditorUtility.DisplayDialog("Carte du serveur", message, "OK");
        }

        private static void OnSceneSaved(Scene scene)
        {
            if (HasMapSource(scene) && Bake(scene, out string message))
            {
                Debug.Log("[Carte du serveur] " + message);
            }
        }

        private static bool HasMapSource(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<MapSource>(true) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Bake(Scene scene, out string message)
        {
            MapSource source = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                source = root.GetComponentInChildren<MapSource>(true);
                if (source != null)
                {
                    break;
                }
            }

            if (source == null)
            {
                message = "Aucun composant MapSource dans la scène « " + scene.name + " ». Ajoute-le sur l'objet Grid.";
                return false;
            }

            MapDefinition definition = source.Read();
            if (definition == null)
            {
                message = "La Tilemap ne contient aucune tuile de sol.";
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            var asset = AssetDatabase.LoadAssetAtPath<MapData>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<MapData>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            asset.Fill(definition);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            message = "Carte " + definition.Grid.Width + "x" + definition.Grid.Height + " enregistrée dans " + AssetPath + ".";
            return true;
        }
    }
}
