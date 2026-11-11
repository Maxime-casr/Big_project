using System.Collections.Generic;
using BigProject.Client;
using BigProject.Core.Characters;
using BigProject.Core.Spells;
using UnityEditor;
using UnityEngine;

namespace BigProject.EditorTools
{
    /// <summary>
    /// Crée les sorts et personnages d'exemple comme de vrais assets (Assets/Resources/Spells et
    /// Assets/Resources/Characters), à partir des catalogues intégrés. Ils servent de modèles : ouvre-les,
    /// modifie-les, ou duplique-les (Ctrl+D) pour en créer de nouveaux. Ne remplace jamais un asset existant.
    /// Création automatique si l'un des deux dossiers n'existe pas encore ; ensuite, via le menu
    /// "Big Project > Contenu > Créer les sorts et personnages d'exemple".
    /// Répare aussi les anciens effets d'invocation (avant l'existence des personnages).
    /// </summary>
    [InitializeOnLoad]
    public static class SpellAssetsInstaller
    {
        private const string ResourcesPath = "Assets/Resources";
        private const string SpellsPath = "Assets/Resources/Spells";
        private const string EffectsPath = "Assets/Resources/Spells/Effets";
        private const string CharactersPath = "Assets/Resources/Characters";

        static SpellAssetsInstaller()
        {
            EditorApplication.delayCall += AutoInstall;
        }

        private static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                (AssetDatabase.IsValidFolder(SpellsPath) && AssetDatabase.IsValidFolder(CharactersPath)))
            {
                return;
            }

            Result result = Install();
            Debug.Log("[Contenu] " + result.Spells + " sort(s) et " + result.Characters + " personnage(s) d'exemple créés, " +
                      result.Repaired + " invocation(s) réparée(s). Modifie-les, ou duplique-en un pour créer le tien.");
        }

        [MenuItem("Big Project/Contenu/Créer les sorts et personnages d'exemple")]
        private static void InstallFromMenu()
        {
            Result result = Install();
            bool nothing = result.Spells == 0 && result.Characters == 0 && result.Repaired == 0;
            EditorUtility.DisplayDialog(
                "Contenu d'exemple",
                nothing
                    ? "Tout existe déjà. Rien n'a été remplacé."
                    : result.Spells + " sort(s) et " + result.Characters + " personnage(s) créé(s), " +
                      result.Repaired + " invocation(s) réparée(s).",
                "OK");
        }

        private struct Result
        {
            public int Spells;
            public int Characters;
            public int Repaired;
        }

        /// <summary>Crée les assets manquants.</summary>
        private static Result Install()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder(ResourcesPath, "Spells");
            EnsureFolder(SpellsPath, "Effets");
            EnsureFolder(ResourcesPath, "Characters");

            SpellCatalog spellCatalog = SpellCatalog.CreateDefault();
            CharacterCatalog characterCatalog = CharacterCatalog.CreateDefault();
            var spellAssets = new Dictionary<string, SpellAsset>();
            var characterAssets = new Dictionary<string, CharacterAsset>();
            var newSpells = new List<string>();
            var newCharacters = new List<string>();

            // 1. Les assets vides : sorts, personnages et invocations se référencent entre eux, ils doivent tous exister d'abord.
            foreach (string id in spellCatalog.AllIds)
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

            foreach (string id in characterCatalog.AllIds)
            {
                string path = CharactersPath + "/" + id + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<CharacterAsset>(path);
                if (existing != null)
                {
                    characterAssets[id] = existing;
                    continue;
                }

                var asset = ScriptableObject.CreateInstance<CharacterAsset>();
                AssetDatabase.CreateAsset(asset, path);
                characterAssets[id] = asset;
                newCharacters.Add(id);
            }

            // 2. Leur contenu.
            foreach (string id in newSpells)
            {
                FillSpell(spellAssets[id], spellCatalog.Get(id), characterAssets);
            }

            foreach (string id in newCharacters)
            {
                FillCharacter(characterAssets[id], characterCatalog.Get(id), spellAssets);
            }

            int repaired = RepairSummonEffects(characterAssets);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new Result { Spells = newSpells.Count, Characters = newCharacters.Count, Repaired = repaired };
        }

        private static void FillCharacter(CharacterAsset asset, CharacterDefinition character, Dictionary<string, SpellAsset> spells)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = character.Id;
            so.FindProperty("displayName").stringValue = character.DisplayName;
            so.FindProperty("playable").boolValue = character.IsPlayable;
            so.FindProperty("maxHealth").intValue = character.MaxHealth;
            so.FindProperty("maxMana").intValue = character.MaxMana;
            so.FindProperty("movementPoints").intValue = character.MovementPoints;

            SerializedProperty list = so.FindProperty("spells");
            list.arraySize = character.SpellIds.Count;
            for (int i = 0; i < character.SpellIds.Count; i++)
            {
                spells.TryGetValue(character.SpellIds[i], out SpellAsset known);
                list.GetArrayElementAtIndex(i).objectReferenceValue = known;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void FillSpell(SpellAsset asset, SpellDefinition spell, Dictionary<string, CharacterAsset> characters)
        {
            var so = new SerializedObject(asset);
            so.FindProperty("id").stringValue = spell.Id;
            so.FindProperty("displayName").stringValue = spell.DisplayName;
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
                effects.GetArrayElementAtIndex(i).objectReferenceValue = GetOrCreateEffect(spell.Effects[i], characters);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static SpellEffectAsset GetOrCreateEffect(SpellEffect effect, Dictionary<string, CharacterAsset> characters)
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
                    characters.TryGetValue(effect.CharacterId, out CharacterAsset summoned);
                    return GetOrCreate<SummonEffectAsset>(
                        "Invoque_" + effect.CharacterId,
                        so => so.FindProperty("character").objectReferenceValue = summoned);
            }
        }

        /// <summary>
        /// Les effets d'invocation d'avant les personnages (« Invocation_Totem ») n'ont pas de personnage : on les
        /// relie à celui qui porte le même nom (Invocation_Totem -> totem). Les autres sont signalés dans la Console.
        /// </summary>
        private static int RepairSummonEffects(Dictionary<string, CharacterAsset> characters)
        {
            int repaired = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:SummonEffectAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var effect = AssetDatabase.LoadAssetAtPath<SummonEffectAsset>(path);
                if (effect == null || effect.Character != null)
                {
                    continue;
                }

                string guess = effect.name;
                int underscore = guess.IndexOf('_');
                if (underscore >= 0)
                {
                    guess = guess.Substring(underscore + 1);
                }

                guess = AssetIds.Slug(guess);
                if (!characters.TryGetValue(guess, out CharacterAsset character))
                {
                    Debug.LogWarning(
                        "[Contenu] L'effet d'invocation « " + effect.name + " » n'a pas de personnage : " +
                        "ouvre-le et glisses-en un dans le champ Character.", effect);
                    continue;
                }

                var so = new SerializedObject(effect);
                so.FindProperty("character").objectReferenceValue = character;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(effect);
                repaired++;
            }

            return repaired;
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
