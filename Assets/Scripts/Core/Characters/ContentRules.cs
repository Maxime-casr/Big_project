using System.Collections.Generic;
using BigProject.Core.Spells;

namespace BigProject.Core.Characters
{
    /// <summary>Vérifications qui croisent les sorts et les personnages, et empreinte du contenu complet.</summary>
    public static class ContentRules
    {
        /// <summary>
        /// Problèmes qui rendent le contenu inutilisable : référence vers un sort ou un personnage qui n'existe
        /// pas, aucun personnage jouable.
        /// </summary>
        public static List<string> FindErrors(SpellCatalog spells, CharacterCatalog characters)
        {
            var errors = new List<string>();

            if (characters.PlayableIds.Count == 0)
            {
                errors.Add("Aucun personnage jouable : coche « Jouable » sur au moins un personnage.");
            }

            foreach (string characterId in characters.AllIds)
            {
                foreach (string spellId in characters.Get(characterId).SpellIds)
                {
                    if (spells.Get(spellId) == null)
                    {
                        errors.Add("Le personnage « " + characterId + " » a le sort « " + spellId + " », qui n'existe pas.");
                    }
                }
            }

            foreach (string spellId in spells.AllIds)
            {
                string summoned = spells.Get(spellId).SummonedCharacterId;
                if (summoned != null && characters.Get(summoned) == null)
                {
                    errors.Add("Le sort « " + spellId + " » invoque « " + summoned + " », qui n'existe pas.");
                }
            }

            return errors;
        }

        /// <summary>Personnages probablement mal réglés : le jeu fonctionne, mais le personnage sera frustrant.</summary>
        public static List<string> FindWarnings(SpellCatalog spells, CharacterCatalog characters)
        {
            var warnings = new List<string>();
            foreach (string characterId in characters.AllIds)
            {
                CharacterDefinition character = characters.Get(characterId);
                if (character.SpellIds.Count == 0)
                {
                    if (character.IsPlayable)
                    {
                        warnings.Add("Le personnage « " + characterId + " » est jouable mais n'a aucun sort.");
                    }

                    continue;
                }

                int cheapest = int.MaxValue;
                foreach (string spellId in character.SpellIds)
                {
                    SpellDefinition spell = spells.Get(spellId);
                    if (spell != null && spell.ManaCost < cheapest)
                    {
                        cheapest = spell.ManaCost;
                    }
                }

                if (cheapest != int.MaxValue && cheapest > character.MaxMana)
                {
                    warnings.Add("Le personnage « " + characterId + " » n'a jamais assez de mana pour lancer un seul de ses sorts.");
                }
            }

            return warnings;
        }

        /// <summary>
        /// Empreinte de tout le contenu de jeu. Le serveur refuse un client dont l'empreinte diffère : les deux
        /// n'appliqueraient pas les mêmes règles.
        /// </summary>
        public static int ComputeHash(SpellCatalog spells, CharacterCatalog characters)
        {
            unchecked
            {
                return spells.ComputeHash() * 31 + characters.ComputeHash();
            }
        }
    }
}
