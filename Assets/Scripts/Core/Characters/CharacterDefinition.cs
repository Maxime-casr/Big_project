using System.Collections.Generic;
using System.Text;

namespace BigProject.Core.Characters
{
    /// <summary>
    /// Un personnage jouable (chevalier, mage, archer...) ou une unité invoquée (totem...) : ses caractéristiques
    /// et les sorts qui forment ses cartes. Ne change jamais pendant un match.
    /// </summary>
    public sealed class CharacterDefinition
    {
        public CharacterDefinition(
            string id,
            string displayName,
            int maxHealth,
            int maxMana,
            int movementPoints,
            IEnumerable<string> spellIds,
            bool isPlayable = true)
        {
            Id = id;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
            MaxHealth = maxHealth;
            MaxMana = maxMana;
            MovementPoints = movementPoints;
            SpellIds = new List<string>(spellIds ?? new string[0]);
            IsPlayable = isPlayable;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int MaxHealth { get; }
        public int MaxMana { get; }

        /// <summary>Points de mouvement par tour.</summary>
        public int MovementPoints { get; }

        /// <summary>Les sorts du personnage, dans l'ordre d'affichage des cartes.</summary>
        public IReadOnlyList<string> SpellIds { get; }

        /// <summary>Peut être choisi par un joueur. Faux pour les unités qui n'existent que par invocation.</summary>
        public bool IsPlayable { get; }

        internal void AppendTo(StringBuilder builder)
        {
            builder.Append(Id).Append('|').Append(MaxHealth).Append('|').Append(MaxMana).Append('|')
                .Append(MovementPoints).Append('|').Append(IsPlayable).Append('|')
                .Append(string.Join(",", SpellIds)).Append(';');
        }
    }
}
