using System;
using System.Collections.Generic;
using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Charge le contenu du jeu (les sorts) depuis les assets. Le serveur, le client en ligne et le combat
    /// local passent tous par ici : ils obtiennent exactement le même catalogue.
    /// </summary>
    public static class GameContent
    {
        public const string SpellsFolder = "Spells";

        private static SpellCatalog spells;
        private static readonly Dictionary<string, SpellAsset> assetsById = new Dictionary<string, SpellAsset>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            spells = null;
            assetsById.Clear();
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
                    "Menu Big Project > Sorts > Créer les sorts d'exemple pour les créer comme assets.");
                spells = SpellCatalog.CreateDefault();
                return spells;
            }

            // Même ordre partout : LoadAll ne garantit rien, et l'ordre des cartes en dépend.
            Array.Sort(assets, (a, b) => string.CompareOrdinal(a.Id, b.Id));

            var catalog = new SpellCatalog();
            foreach (SpellAsset asset in assets)
            {
                try
                {
                    catalog.Add(asset.ToDefinition());
                    assetsById[asset.Id] = asset;
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogError("Sort « " + asset.name + " » ignoré : " + exception.Message, asset);
                }
            }

            foreach (string problem in catalog.Validate())
            {
                Debug.LogError(problem);
            }

            spells = catalog;
            return spells;
        }

        /// <summary>L'icône d'un sort, ou null s'il n'en a pas.</summary>
        public static Sprite GetIcon(string spellId)
        {
            LoadSpells();
            return assetsById.TryGetValue(spellId, out SpellAsset asset) ? asset.Icon : null;
        }

        /// <summary>La description écrite dans l'asset du sort, ou une chaîne vide.</summary>
        public static string GetDescription(string spellId)
        {
            LoadSpells();
            return assetsById.TryGetValue(spellId, out SpellAsset asset) ? asset.Description : string.Empty;
        }
    }
}
