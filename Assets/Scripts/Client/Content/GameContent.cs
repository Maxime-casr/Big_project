using System;
using System.Collections.Generic;
using BigProject.Core.Characters;
using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Charge le contenu du jeu (sorts et personnages) depuis les assets. Le serveur, le client en ligne et le
    /// combat local passent tous par ici : ils obtiennent exactement le même contenu.
    /// </summary>
    public static class GameContent
    {
        public const string SpellsFolder = "Spells";
        public const string CharactersFolder = "Characters";

        /// <summary>Un personnage tel que le menu de choix l'affiche.</summary>
        public sealed class CharacterInfo
        {
            public string Id;
            public string Name;
            public string Summary;
            public Sprite Icon;
        }

        private static SpellCatalog spells;
        private static CharacterCatalog characters;
        private static readonly Dictionary<string, SpellAsset> spellAssets = new Dictionary<string, SpellAsset>();
        private static readonly Dictionary<string, CharacterAsset> characterAssets = new Dictionary<string, CharacterAsset>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            spells = null;
            characters = null;
            spellAssets.Clear();
            characterAssets.Clear();
        }

        /// <summary>Le catalogue des sorts, construit une seule fois à partir de Resources/Spells.</summary>
        public static SpellCatalog LoadSpells()
        {
            if (spells != null)
            {
                return spells;
            }

            SpellAsset[] assets = Resources.LoadAll<SpellAsset>(SpellsFolder);
            if (assets.Length == 0)
            {
                Debug.LogWarning(
                    "Aucun sort dans Assets/Resources/" + SpellsFolder + " : sorts d'exemple intégrés utilisés. " +
                    "Menu Big Project > Contenu > Créer les sorts et personnages d'exemple.");
                spells = SpellCatalog.CreateDefault();
                return spells;
            }

            // Même ordre partout : LoadAll ne garantit rien.
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var catalog = new SpellCatalog();
            foreach (SpellAsset asset in assets)
            {
                try
                {
                    catalog.Add(asset.ToDefinition());
                    spellAssets[asset.Id] = asset;
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogError("Sort « " + asset.name + " » ignoré : " + exception.Message, asset);
                }
            }

            spells = catalog;
            return spells;
        }

        /// <summary>
        /// Le catalogue des personnages, construit une seule fois à partir de Resources/Characters. Les problèmes
        /// (sort inexistant, aucun personnage jouable...) sont signalés dans la Console au premier chargement.
        /// </summary>
        public static CharacterCatalog LoadCharacters()
        {
            if (characters != null)
            {
                return characters;
            }

            SpellCatalog spellCatalog = LoadSpells();
            CharacterAsset[] assets = Resources.LoadAll<CharacterAsset>(CharactersFolder);
            if (assets.Length == 0)
            {
                Debug.LogWarning(
                    "Aucun personnage dans Assets/Resources/" + CharactersFolder + " : personnages d'exemple intégrés utilisés. " +
                    "Menu Big Project > Contenu > Créer les sorts et personnages d'exemple.");
                characters = CharacterCatalog.CreateDefault();
                Report(spellCatalog, characters);
                return characters;
            }

            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var catalog = new CharacterCatalog();
            foreach (CharacterAsset asset in assets)
            {
                try
                {
                    catalog.Add(asset.ToDefinition());
                    characterAssets[asset.Id] = asset;
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogError("Personnage « " + asset.name + " » ignoré : " + exception.Message, asset);
                }
            }

            characters = catalog;
            Report(spellCatalog, characters);
            return characters;
        }

        private static void Report(SpellCatalog spellCatalog, CharacterCatalog characterCatalog)
        {
            foreach (string error in ContentRules.FindErrors(spellCatalog, characterCatalog))
            {
                Debug.LogError("[Contenu] " + error);
            }

            foreach (string warning in ContentRules.FindWarnings(spellCatalog, characterCatalog))
            {
                Debug.LogWarning("[Contenu] " + warning);
            }
        }

        /// <summary>Les personnages choisissables, dans l'ordre alphabétique de leur identifiant.</summary>
        public static List<CharacterInfo> GetPlayableCharacters()
        {
            CharacterCatalog catalog = LoadCharacters();
            SpellCatalog spellCatalog = LoadSpells();
            var result = new List<CharacterInfo>();

            foreach (string id in catalog.PlayableIds)
            {
                CharacterDefinition definition = catalog.Get(id);
                var spellNames = new List<string>();
                foreach (string spellId in definition.SpellIds)
                {
                    SpellDefinition spell = spellCatalog.Get(spellId);
                    spellNames.Add(spell != null ? spell.DisplayName : spellId);
                }

                result.Add(new CharacterInfo
                {
                    Id = id,
                    Name = definition.DisplayName,
                    Summary = "PV " + definition.MaxHealth + " · Mana " + definition.MaxMana + " · PM " + definition.MovementPoints +
                              "\n" + string.Join(", ", spellNames),
                    Icon = GetCharacterIcon(id)
                });
            }

            return result;
        }

        /// <summary>Le prefab d'un personnage, ou null s'il n'en a pas (le prefab par défaut ou un carré prend le relais).</summary>
        public static GameObject GetCharacterPrefab(string characterId)
        {
            LoadCharacters();
            return characterAssets.TryGetValue(characterId ?? string.Empty, out CharacterAsset asset) ? asset.Prefab : null;
        }

        public static Sprite GetCharacterIcon(string characterId)
        {
            LoadCharacters();
            return characterAssets.TryGetValue(characterId ?? string.Empty, out CharacterAsset asset) ? asset.Icon : null;
        }

        /// <summary>Le nom affichable d'un personnage, son identifiant s'il est inconnu.</summary>
        public static string GetCharacterName(string characterId)
        {
            CharacterDefinition definition = LoadCharacters().Get(characterId);
            return definition != null ? definition.DisplayName : characterId;
        }

        /// <summary>L'icône d'un sort, ou null s'il n'en a pas.</summary>
        public static Sprite GetIcon(string spellId)
        {
            LoadSpells();
            return spellAssets.TryGetValue(spellId, out SpellAsset asset) ? asset.Icon : null;
        }

        /// <summary>La description écrite dans l'asset du sort, ou une chaîne vide.</summary>
        public static string GetDescription(string spellId)
        {
            LoadSpells();
            return spellAssets.TryGetValue(spellId, out SpellAsset asset) ? asset.Description : string.Empty;
        }
    }
}
