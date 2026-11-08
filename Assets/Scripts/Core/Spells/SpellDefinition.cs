using System;
using System.Collections.Generic;

namespace BigProject.Core.Spells
{
    public enum AreaShape
    {
        /// <summary>Uniquement la case visée.</summary>
        Single,
        /// <summary>La case visée et ses 4 voisines, jusqu'au rayon indiqué.</summary>
        Cross,
        /// <summary>Toutes les cases à distance (Manhattan) inférieure ou égale au rayon.</summary>
        Diamond
    }

    [Flags]
    public enum TargetFilter
    {
        None = 0,
        Self = 1,
        Allies = 2,
        Enemies = 4,
        Everyone = Self | Allies | Enemies
    }

    public enum SpellEffectKind
    {
        Damage,
        Heal
    }

    public sealed class SpellEffect
    {
        public SpellEffect(SpellEffectKind kind, int amount, TargetFilter targets)
        {
            Kind = kind;
            Amount = amount;
            Targets = targets;
        }

        public SpellEffectKind Kind { get; }
        public int Amount { get; }
        public TargetFilter Targets { get; }
    }

    /// <summary>Description statique d'un sort. Ne change jamais pendant un match.</summary>
    public sealed class SpellDefinition
    {
        public SpellDefinition(
            string id,
            string displayName,
            int manaCost,
            int minRange,
            int maxRange,
            bool requiresLineOfSight,
            bool straightLineOnly,
            AreaShape areaShape,
            int areaRadius,
            IEnumerable<SpellEffect> effects)
        {
            Id = id;
            DisplayName = displayName;
            ManaCost = manaCost;
            MinRange = minRange;
            MaxRange = maxRange;
            RequiresLineOfSight = requiresLineOfSight;
            StraightLineOnly = straightLineOnly;
            AreaShape = areaShape;
            AreaRadius = areaRadius;
            Effects = new List<SpellEffect>(effects);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int ManaCost { get; }
        public int MinRange { get; }
        public int MaxRange { get; }
        public bool RequiresLineOfSight { get; }
        /// <summary>Le sort ne peut viser que les cases alignées avec le lanceur.</summary>
        public bool StraightLineOnly { get; }
        public AreaShape AreaShape { get; }
        public int AreaRadius { get; }
        public IReadOnlyList<SpellEffect> Effects { get; }
    }
}
