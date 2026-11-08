using System;
using System.Collections.Generic;

namespace BigProject.Core.Spells
{
    /// <summary>Registre des sorts du jeu. Le serveur et les clients doivent utiliser le même contenu.</summary>
    public sealed class SpellCatalog
    {
        private readonly Dictionary<string, SpellDefinition> byId = new Dictionary<string, SpellDefinition>();
        private readonly List<string> orderedIds = new List<string>();

        public IReadOnlyList<string> AllIds => orderedIds;

        public void Add(SpellDefinition spell)
        {
            if (spell == null)
            {
                throw new ArgumentNullException(nameof(spell));
            }

            if (byId.ContainsKey(spell.Id))
            {
                throw new InvalidOperationException("Sort déjà enregistré : " + spell.Id);
            }

            byId.Add(spell.Id, spell);
            orderedIds.Add(spell.Id);
        }

        /// <summary>Retourne null si le sort n'existe pas.</summary>
        public SpellDefinition Get(string id)
        {
            return id != null && byId.TryGetValue(id, out SpellDefinition spell) ? spell : null;
        }

        public static SpellCatalog CreateDefault()
        {
            var catalog = new SpellCatalog();

            catalog.Add(new SpellDefinition(
                "fireball", "Boule de feu", manaCost: 3, minRange: 2, maxRange: 5,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { new SpellEffect(SpellEffectKind.Damage, 6, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "piercing_arrow", "Flèche perçante", manaCost: 2, minRange: 1, maxRange: 6,
                requiresLineOfSight: true, straightLineOnly: true,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { new SpellEffect(SpellEffectKind.Damage, 4, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "thunder_cross", "Foudre en croix", manaCost: 4, minRange: 1, maxRange: 4,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Cross, areaRadius: 1,
                effects: new[] { new SpellEffect(SpellEffectKind.Damage, 3, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "mend", "Soin", manaCost: 2, minRange: 0, maxRange: 3,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { new SpellEffect(SpellEffectKind.Heal, 5, TargetFilter.Self | TargetFilter.Allies) }));

            return catalog;
        }
    }
}
