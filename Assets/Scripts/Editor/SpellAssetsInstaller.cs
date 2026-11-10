using System.Collections.Generic;
using BigProject.Client;
using BigProject.Core.Spells;
using UnityEditor;
using UnityEngine;

namespace BigProject.EditorTools
{
    /// <summary>
    /// Crée les sorts d'exemple comme de vrais assets (Assets/Resources/Spells), à partir du catalogue intégré.
    /// Ils servent de modèles : ouvre-les, modifie-les, ou duplique-les (Ctrl+D) pour en créer de nouveaux.
    /// Création automatique une seule fois, si le dossier Resources/Spells n'existe pas encore ; ensuite,
    /// uniquement via le menu "Big Project > Sorts > Créer les sorts d'exemple" (qui ne remplace jamais rien).
    /// </summary>
    [InitializeOnLoad]
    public static class SpellAssetsInstaller
    {
        private const string ResourcesPath = "Assets/Resources";
        private const string SpellsPath = "Assets/Resources/Spells";
        private const string EffectsPath = "Assets/Resources/Spells/Effets";

        static SpellAssetsInstaller()
        {
            EditorApplication.delayCall += AutoInstall;
        }

        private static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                AssetDatabase.IsValidFolder(SpellsPath))
            {
                return;
            }

            int created = Install();
            Debug.Log("[Sorts] " + created + " sorts d'exemple créés dans " + SpellsPath +
                      ". Modifie-les, ou duplique-en un pour créer le tien.");
        }

        [MenuItem("Big Project/Sorts/Créer les sorts d'exemple")]
        private static void InstallFromMenu()
        {
            int created = Install();
            EditorUtility.DisplayDialog(
                "Sorts d'exemple",
                created == 0
                    ? "Tous les sorts d'exemple existent déjà. Rien n'a été remplacé."
                    : created + " sort(s) créé(s) dans " + SpellsPath + ".",
                "OK");
        }

        /// <summary>Crée les assets manquants. Retourne le nombre de sorts créés.</summary>
        private static int Install()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder(ResourcesPath, "Spells");
            EnsureFolder(SpellsPath, "Effets");

            SpellCatalog catalog = SpellCatalog.CreateDefault();
            var spellAssets = new Dictionary<string, SpellAsset>();
            var newSpells = new List<string>();

            // 1. Les sorts eux-mêmes : une invocation en référence d'autres, il faut qu'ils existent tous d'abord.
            foreach (string id in catalog.AllIds)
            {
                string path = SpellsPath + "/" + id + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<SpellAsset>(path);
                if (existing != null)
                {
                    spellAssets[id] = existing;
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<SpellAsset>();
                AssetDatabase.CreateAsset(asset, path);
                spellAssets[id] = asset;
                newSpells.Add(id);
            }

            // 2. Leur contenu.
            foreach (string id in newSpells)
            {
                Fill(spellAssets[id], catalog.Get(id), spellAssets);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return newSpells.Count;
        }

        private static void Fill(SpellAsset asset, SpellDefinition spell, Dictionary<string, SpellAsset> allSpells)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = spell.Id;
            so.FindProperty("displayName").stringValue = spell.DisplayName;
            so.FindProperty("givenToEveryone").boolValue = spell.IsStartingSpell;
            so.FindProperty("manaCost").intValue = spell.ManaCost;
            so.FindProperty("targeting").enumValueIndex = (int)spell.Targeting;
            so.FindProperty("unitTargets").enumValueFlag = (int)spell.UnitTargets;
            so.FindProperty("minRange").intValue = spell.MinRange;
            so.FindProperty("maxRange").intValue = spell.MaxRange;
            so.FindProperty("requiresLineOfSight").boolValue = spell.RequiresLineOfSight;
            so.FindProperty("straightLineOnly").boolValue = spell.StraightLineOnly;
            so.FindProperty("areaShape").enumValueIndex = (int)spell.AreaShape;
            so.FindProperty("areaRadius").intValue = spell.AreaRadius;

            SerializedProperty effects = so.FindProperty("effects");
            effects.arraySize = spell.Effects.Count;
            for (int i = 0; i < spell.Effects.Count; i++)
            {
                effects.GetArrayElementAtIndex(i).objectReferenceValue = GetOrCreateEffect(spell.Effects[i], allSpells);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static SpellEffectAsset GetOrCreateEffect(SpellEffect effect, Dictionary<string, SpellAsset> allSpells)
        {
            switch (effect.Kind)
            {
                case SpellEffectKind.Damage:
                    return GetOrCreate<DamageEffectAsset>(
                        "Degats_" + effect.Amount + "_" + FilterName(effect.Targets),
                        so =>
                        {
                            so.FindProperty("amount").intValue = effect.Amount;
                            so.FindProperty("targets").enumValueFlag = (int)effect.Targets;
                        });

                case SpellEffectKind.Heal:
                    return GetOrCreate<HealEffectAsset>(
                        "Soin_" + effect.Amount + "_" + FilterName(effect.Targets),
                        so =>
                        {
                            so.FindProperty("amount").intValue = effect.Amount;
                            so.FindProperty("targets").enumValueFlag = (int)effect.Targets;
                        });

                case SpellEffectKind.Teleport:
                    return GetOrCreate<TeleportEffectAsset>("Teleportation", null);

                default:
                    SummonTemplate template = effect.Summon;
                    return GetOrCreate<SummonEffectAsset>(
                        "Invocation_" + template.Name,
                        so =>
                        {
                            so.FindProperty("summonName").stringValue = template.Name;
                            so.FindProperty("maxHealth").intValue = template.MaxHealth;
                            so.FindProperty("maxMana").intValue = template.MaxMana;
                            so.FindProperty("movementPoints").intValue = template.MovementPoints;

                            SerializedProperty list = so.FindProperty("spells");
                            list.arraySize = template.SpellIds.Count;
                            for (int i = 0; i < template.SpellIds.Count; i++)
                            {
                                allSpells.TryGetValue(template.SpellIds[i], out SpellAsset known);
                                list.GetArrayElementAtIndex(i).objectReferenceValue = known;
                            }
                        });
            }
        }

        /// <summary>L'effet de ce nom s'il existe déjà (réutilisé par plusieurs sorts), sinon il est créé.</summary>
        private static T GetOrCreate<T>(string fileName, System.Action<SerializedObject> configure)
            where T : SpellEffectAsset
        {
            string path = EffectsPath + "/" + fileName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);

            if (configure != null)
            {
                var so = new SerializedObject(asset);
                configure(so);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static string FilterName(TargetFilter filter)
        {
            return filter.ToString().Replace(", ", "_").Replace(" ", string.Empty);
        }

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
