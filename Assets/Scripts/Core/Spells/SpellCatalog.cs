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

        public void Add(SpellDefinition spell)
        {
            if (spell == null)
            {
                throw new ArgumentNullException(nameof(spell));
            }

            if (!IdRules.IsValid(spell.Id))
            {
                throw new InvalidOperationException(
                    "Identifiant de sort invalide « " + spell.Id + " » (" + IdRules.Description + ").");
            }

            if (byId.ContainsKey(spell.Id))
            {
                throw new InvalidOperationException("Sort déjà enregistré : " + spell.Id);
            }

            if (spell.MinRange < 0 || spell.MaxRange < spell.MinRange || spell.ManaCost < 0 || spell.AreaRadius < 0)
            {
                throw new InvalidOperationException("Valeurs invalides (portée, coût ou zone) pour le sort " + spell.Id + ".");
            }

            bool teleports = spell.HasEffect(SpellEffectKind.Teleport);
            bool summons = spell.HasEffect(SpellEffectKind.Summon);

            if ((teleports || summons) && spell.Targeting != TargetKind.Cell)
            {
                throw new InvalidOperationException(
                    "Le sort " + spell.Id + " téléporte ou invoque : il doit viser une case (type Cell).");
            }

            // Une téléportation et une invocation visent toutes deux la case ciblée : en combiner ou en
            // multiplier les ferait se bloquer entre elles.
            if (spell.CountEffects(SpellEffectKind.Teleport) + spell.CountEffects(SpellEffectKind.Summon) > 1)
            {
                throw new InvalidOperationException(
                    "Le sort " + spell.Id + " a plusieurs effets de téléportation ou d'invocation : un seul est permis.");
            }

            foreach (SpellEffect effect in spell.Effects)
            {
                if (effect.Kind == SpellEffectKind.Summon && !IdRules.IsValid(effect.CharacterId))
                {
                    throw new InvalidOperationException("Le sort " + spell.Id + " invoque sans personnage valide.");
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

            return Hashing.Fnv1a(builder.ToString());
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
                effects: new[] { SpellEffect.SummonCharacter("totem") }));

            catalog.Add(new SpellDefinition(
                "sword_strike", "Coup d'épée", manaCost: 2, minRange: 1, maxRange: 1,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Damage(8, TargetFilter.Enemies) },
                targeting: TargetKind.Unit, unitTargets: TargetFilter.Enemies));

            catalog.Add(new SpellDefinition(
                "charge", "Charge", manaCost: 3, minRange: 2, maxRange: 3,
                requiresLineOfSight: false, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Teleport() }));

            catalog.Add(new SpellDefinition(
                "quick_shot", "Tir rapide", manaCost: 1, minRange: 2, maxRange: 6,
                requiresLineOfSight: true, straightLineOnly: false,
                areaShape: AreaShape.Single, areaRadius: 0,
                effects: new[] { SpellEffect.Damage(3, TargetFilter.Enemies) },
                targeting: TargetKind.Unit, unitTargets: TargetFilter.Enemies));

            return catalog;
        }
    }
}
