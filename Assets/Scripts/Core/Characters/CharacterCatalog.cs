using System;
using System.Collections.Generic;
using System.Text;
using BigProject.Core.Net;

namespace BigProject.Core.Characters
{
    /// <summary>Registre des personnages du jeu. Le serveur et les clients doivent utiliser exactement le même contenu.</summary>
    public sealed class CharacterCatalog
    {
        private readonly Dictionary<string, CharacterDefinition> byId = new Dictionary<string, CharacterDefinition>();
        private readonly List<string> orderedIds = new List<string>();

        public IReadOnlyList<string> AllIds => orderedIds;

        /// <summary>Les personnages qu'un joueur peut choisir, dans l'ordre d'ajout.</summary>
        public IReadOnlyList<string> PlayableIds
        {
            get
            {
                var ids = new List<string>();
                foreach (string id in orderedIds)
                {
                    if (byId[id].IsPlayable)
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
        }

        public void Add(CharacterDefinition character)
        {
            if (character == null)
            {
                throw new ArgumentNullException(nameof(character));
            }

            if (!IdRules.IsValid(character.Id))
            {
                throw new InvalidOperationException(
                    "Identifiant de personnage invalide « " + character.Id + " » (" + IdRules.Description + ").");
            }

            if (byId.ContainsKey(character.Id))
            {
                throw new InvalidOperationException("Personnage déjà enregistré : " + character.Id);
            }

            if (character.MaxHealth <= 0 || character.MaxMana < 0 || character.MovementPoints < 0)
            {
                throw new InvalidOperationException("Valeurs invalides (PV, mana ou PM) pour le personnage " + character.Id + ".");
            }

            if (character.SpellIds.Count > NetLimits.MaxSpellsPerUnit)
            {
                throw new InvalidOperationException(
                    "Trop de sorts pour le personnage " + character.Id + " (maximum " + NetLimits.MaxSpellsPerUnit + ").");
            }

            var seen = new HashSet<string>();
            foreach (string spellId in character.SpellIds)
            {
                if (!seen.Add(spellId))
                {
                    throw new InvalidOperationException(
                        "Le personnage " + character.Id + " a deux fois le sort « " + spellId + " ».");
                }
            }

            byId.Add(character.Id, character);
            orderedIds.Add(character.Id);
        }

        /// <summary>Retourne null si le personnage n'existe pas.</summary>
        public CharacterDefinition Get(string id)
        {
            return id != null && byId.TryGetValue(id, out CharacterDefinition character) ? character : null;
        }

        /// <summary>Empreinte du contenu, indépendante de l'ordre d'ajout.</summary>
        public int ComputeHash()
        {
            var ids = new List<string>(orderedIds);
            ids.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (string id in ids)
            {
                byId[id].AppendTo(builder);
            }

            return Hashing.Fnv1a(builder.ToString());
        }

        /// <summary>
        /// Personnages d'exemple, à partir des sorts d'exemple (SpellCatalog.CreateDefault). Les assets de
        /// l'éditeur sont créés à partir de cette liste au premier lancement.
        /// </summary>
        public static CharacterCatalog CreateDefault()
        {
            var catalog = new CharacterCatalog();

            catalog.Add(new CharacterDefinition(
                "knight", "Chevalier", maxHealth: 40, maxMana: 6, movementPoints: 3,
                spellIds: new[] { "sword_strike", "charge", "mend" }));

            catalog.Add(new CharacterDefinition(
                "mage", "Mage", maxHealth: 22, maxMana: 10, movementPoints: 3,
                spellIds: new[] { "fireball", "thunder_cross", "blink", "seeking_bolt" }));

            catalog.Add(new CharacterDefinition(
                "archer", "Archer", maxHealth: 28, maxMana: 8, movementPoints: 4,
                spellIds: new[] { "piercing_arrow", "quick_shot", "totem" }));

            catalog.Add(new CharacterDefinition(
                "totem", "Totem", maxHealth: 12, maxMana: 4, movementPoints: 0,
                spellIds: new[] { "piercing_arrow" }, isPlayable: false));

            return catalog;
        }
    }
}
