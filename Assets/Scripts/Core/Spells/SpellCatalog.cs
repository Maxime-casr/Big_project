using System;
using System.Collections.Generic;
using System.Text;

namespace BigProject.Core.Spells
{
    /// <summary>Registre des sorts du jeu. Le serveur et les clients doivent utiliser exactement le même contenu.</summary>
    public sealed class SpellCatalog
    {
        private readonly Dictionary<string, SpellDefinition> byId = new Dictionary<string, SpellDefinition>();
        private readonly List<string> orderedIds = new List<string>();

        public IReadOnlyList<string> AllIds => orderedIds;

        /// <summary>Les sorts donnés à chaque personnage au début d'un match, dans l'ordre d'ajout.</summary>
        public IReadOnlyList<string> StartingIds
        {
            get
            {
                var ids = new List<string>();
                foreach (string id in orderedIds)
                {
                    if (byId[id].IsStartingSpell)
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
        }

        public void Add(SpellDefinition spell)
        {
            if (spell == null)
            {
                throw new ArgumentNullException(nameof(spell));
            }

            if (string.IsNullOrWhiteSpace(spell.Id))
            {
                throw new InvalidOperationException("Un sort n'a pas d'identifiant.");
            }

            if (byId.ContainsKey(spell.Id))
            {
                throw new InvalidOperationException("Sort déjà enregistré : " + spell.Id);
            }

            if (spell.MinRange < 0 || spell.MaxRange < spell.MinRange || spell.ManaCost < 0 || spell.AreaRadius < 0)
            {
                throw new InvalidOperationException("Valeurs invalides (portée, coût ou zone) pour le sort " + spell.Id + ".");
            }

            if ((spell.HasEffect(SpellEffectKind.Teleport) || spell.HasEffect(SpellEffectKind.Summon)) &&
                spell.Targeting != TargetKind.Cell)
            {
                throw new InvalidOperationException(
                    "Le sort " + spell.Id + " téléporte ou invoque : il doit viser une case (type Cell).");
            }

            foreach (SpellEffect effect in spell.Effects)
            {
                if (effect.Kind == SpellEffectKind.Summon && effect.Summon == null)
                {
                    throw new InvalidOperationException("Le sort " + spell.Id + " invoque sans définir l'invocation.");
                }
            }

            byId.Add(spell.Id, spell);
            orderedIds.Add(spell.Id);
        }

        /// <summary>Retourne null si le sort n'existe pas.</summary>
        public SpellDefinition Get(string id)
        {
            return id != null && byId.TryGetValue(id, out SpellDefinition spell) ? spell : null;
        }

        /// <summary>Problèmes de cohérence du catalogue (sorts d'invocation qui n'existent pas...). Vide si tout va bien.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            foreach (string id in orderedIds)
            {
                foreach (SpellEffect effect in byId[id].Effects)
                {
                    if (effect.Summon == null)
                    {
                        continue;
                    }

                    foreach (string summonSpell in effect.Summon.SpellIds)
                    {
                        if (!byId.ContainsKey(summonSpell))
                        {
                            problems.Add("Le sort " + id + " invoque une unité qui connaît « " + summonSpell + " », introuvable.");
                        }
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Empreinte du contenu, indépendante de l'ordre d'ajout. Le serveur refuse un client dont les sorts
        /// diffèrent des siens : sans cela, les deux n'appliqueraient pas les mêmes règles.
        /// </summary>
        public int ComputeHash()
        {
            var ids = new List<string>(orderedIds);
            ids.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (string id in ids)
            {
                byId[id].AppendTo(builder);
            }

            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in builder.ToString())
                {
                    hash = (hash ^ c) * 16777619;
                }

                return hash;
            }
        }

        /// <summary>Sorts d'exemple. Les assets de l'éditeur sont créés à partir de cette liste au premier lancement.</summary>
        public static SpellCatalog CreateDefault()
        {
            var catalog = new SpellCatalog();

            catalog.Add(new SpellDefinition(
                "fireball", "Boule de feu", manaCost: 3, minRange: 2, maxRange: 5,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Damage(6, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "piercing_arrow", "Flèche perçante", manaCost: 2, minRange: 1, maxRange: 6,
                requiresLineOfSight: true, straightLineOnly: true,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Damage(4, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "thunder_cross", "Foudre en croix", manaCost: 4, minRange: 1, maxRange: 4,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Cross, areaRadius: 1,
                effects: new[] { SpellEffect.Damage(3, TargetFilter.Enemies) }));

            catalog.Add(new SpellDefinition(
                "mend", "Soin", manaCost: 2, minRange: 0, maxRange: 3,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Heal(5, TargetFilter.Self | TargetFilter.Allies) }));

            catalog.Add(new SpellDefinition(
                "seeking_bolt", "Éclair ciblé", manaCost: 3, minRange: 0, maxRange: 4,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Damage(5, TargetFilter.Enemies) },
                targeting: TargetKind.Unit, unitTargets: TargetFilter.Enemies));

            catalog.Add(new SpellDefinition(
                "blink", "Saut", manaCost: 3, minRange: 1, maxRange: 4,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Teleport() }));

            catalog.Add(new SpellDefinition(
                "totem", "Totem", manaCost: 4, minRange: 1, maxRange: 3,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[]
                {
                    SpellEffect.SummonUnit(new SummonTemplate("Totem", maxHealth: 12, maxMana: 4, movementPoints: 0,
                        spellIds: new[] { "piercing_arrow" }))
                }));

            return catalog;
        }
    }
}
