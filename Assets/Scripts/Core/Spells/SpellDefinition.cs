using System;
using System.Collections.Generic;
using System.Text;

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

    /// <summary>Ce que le joueur désigne quand il planifie le sort.</summary>
    public enum TargetKind
    {
        /// <summary>Une case. L'effet frappe ce qui s'y trouve à la fin de l'action.</summary>
        Cell = 0,

        /// <summary>
        /// Une unité (ennemie, alliée ou soi-même, selon le sort). Le sort la suit : il la touche où qu'elle
        /// soit à la fin de l'action, mais n'est lancé que si elle est à portée au début de l'action.
        /// </summary>
        Unit = 1,

        /// <summary>Aucune cible : le sort s'applique au lanceur.</summary>
        Self = 2
    }

    public enum SpellEffectKind
    {
        Damage = 0,
        Heal = 1,

        /// <summary>Le lanceur apparaît sur la case visée (sort de type Cell).</summary>
        Teleport = 2,

        /// <summary>Une nouvelle unité alliée apparaît sur la case visée (sort de type Cell).</summary>
        Summon = 3
    }

    /// <summary>Ce qu'est, et ce que sait faire, une unité invoquée.</summary>
    public sealed class SummonTemplate
    {
        public SummonTemplate(string name, int maxHealth, int maxMana, int movementPoints, IEnumerable<string> spellIds)
        {
            Name = name;
            MaxHealth = maxHealth;
            MaxMana = maxMana;
            MovementPoints = movementPoints;
            SpellIds = new List<string>(spellIds ?? new string[0]);
        }

        public string Name { get; }
        public int MaxHealth { get; }
        public int MaxMana { get; }
        public int MovementPoints { get; }
        public IReadOnlyList<string> SpellIds { get; }
    }

    public sealed class SpellEffect
    {
        public SpellEffect(SpellEffectKind kind, int amount, TargetFilter targets, SummonTemplate summon = null)
        {
            Kind = kind;
            Amount = amount;
            Targets = targets;
            Summon = summon;
        }

        public SpellEffectKind Kind { get; }
        public int Amount { get; }
        public TargetFilter Targets { get; }
        public SummonTemplate Summon { get; }

        public static SpellEffect Damage(int amount, TargetFilter targets)
        {
            return new SpellEffect(SpellEffectKind.Damage, amount, targets);
        }

        public static SpellEffect Heal(int amount, TargetFilter targets)
        {
            return new SpellEffect(SpellEffectKind.Heal, amount, targets);
        }

        public static SpellEffect Teleport()
        {
            return new SpellEffect(SpellEffectKind.Teleport, 0, TargetFilter.Self);
        }

        public static SpellEffect SummonUnit(SummonTemplate template)
        {
            return new SpellEffect(SpellEffectKind.Summon, 0, TargetFilter.None, template);
        }

        /// <summary>Effets qui agissent sur les PV à l'impact (les autres placent des unités au début de l'action).</summary>
        public bool IsImpact => Kind == SpellEffectKind.Damage || Kind == SpellEffectKind.Heal;
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
            IEnumerable<SpellEffect> effects,
            TargetKind targeting = TargetKind.Cell,
            TargetFilter unitTargets = TargetFilter.Enemies,
            bool isStartingSpell = true)
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
            Targeting = targeting;
            UnitTargets = unitTargets;
            IsStartingSpell = isStartingSpell;
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
        public TargetKind Targeting { get; }

        /// <summary>Qui peut être désigné (sorts de type Unit seulement).</summary>
        public TargetFilter UnitTargets { get; }

        /// <summary>Donné à chaque personnage au début d'un match.</summary>
        public bool IsStartingSpell { get; }

        public bool HasEffect(SpellEffectKind kind)
        {
            foreach (SpellEffect effect in Effects)
            {
                if (effect.Kind == kind)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Résumé court des effets, pour les cartes de l'interface.</summary>
        public string DescribeEffects()
        {
            var parts = new List<string>();
            foreach (SpellEffect effect in Effects)
            {
                switch (effect.Kind)
                {
                    case SpellEffectKind.Damage:
                        parts.Add(effect.Amount + " dégâts");
                        break;
                    case SpellEffectKind.Heal:
                        parts.Add(effect.Amount + " soin");
                        break;
                    case SpellEffectKind.Teleport:
                        parts.Add("téléportation");
                        break;
                    case SpellEffectKind.Summon:
                        parts.Add("invoque " + effect.Summon.Name);
                        break;
                }
            }

            return string.Join(", ", parts);
        }

        /// <summary>Ce que le joueur doit viser, en quelques mots.</summary>
        public string DescribeTargeting()
        {
            string range = MinRange == MaxRange ? MaxRange.ToString() : MinRange + "-" + MaxRange;
            switch (Targeting)
            {
                case TargetKind.Self:
                    return "sur soi";
                case TargetKind.Unit:
                    return "unité, portée " + range;
                default:
                    return "case, portée " + range;
            }
        }

        internal void AppendTo(StringBuilder builder)
        {
            builder.Append(Id).Append('|').Append(ManaCost).Append('|').Append(MinRange).Append('|').Append(MaxRange)
                .Append('|').Append(RequiresLineOfSight).Append('|').Append(StraightLineOnly)
                .Append('|').Append((int)AreaShape).Append('|').Append(AreaRadius).Append('|').Append((int)Targeting)
                .Append('|').Append((int)UnitTargets).Append('|').Append(IsStartingSpell);

            foreach (SpellEffect effect in Effects)
            {
                builder.Append('[').Append((int)effect.Kind).Append(',').Append(effect.Amount).Append(',')
                    .Append((int)effect.Targets);
                if (effect.Summon != null)
                {
                    builder.Append(',').Append(effect.Summon.Name).Append(',').Append(effect.Summon.MaxHealth)
                        .Append(',').Append(effect.Summon.MaxMana).Append(',').Append(effect.Summon.MovementPoints)
                        .Append(',').Append(string.Join("+", effect.Summon.SpellIds));
                }

                builder.Append(']');
            }

            builder.Append(';');
        }
    }
}
