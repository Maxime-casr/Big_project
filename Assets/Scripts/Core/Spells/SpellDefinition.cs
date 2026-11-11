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

        /// <summary>Un personnage apparaît sur la case visée, allié du lanceur (sort de type Cell).</summary>
        Summon = 3
    }

    public sealed class SpellEffect
    {
        public SpellEffect(SpellEffectKind kind, int amount, TargetFilter targets, string characterId = null)
        {
            Kind = kind;
            Amount = amount;
            Targets = targets;
            CharacterId = characterId;
        }

        public SpellEffectKind Kind { get; }
        public int Amount { get; }
        public TargetFilter Targets { get; }

        /// <summary>Le personnage invoqué (effet d'invocation seulement).</summary>
        public string CharacterId { get; }

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

        public static SpellEffect SummonCharacter(string characterId)
        {
            return new SpellEffect(SpellEffectKind.Summon, 0, TargetFilter.None, characterId);
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
            TargetFilter unitTargets = TargetFilter.Enemies)
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

        public bool HasEffect(SpellEffectKind kind)
        {
            return CountEffects(kind) > 0;
        }

        public int CountEffects(SpellEffectKind kind)
        {
            int count = 0;
            foreach (SpellEffect effect in Effects)
            {
                if (effect.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Le personnage invoqué par ce sort, ou null.</summary>
        public string SummonedCharacterId
        {
            get
            {
                foreach (SpellEffect effect in Effects)
                {
                    if (effect.Kind == SpellEffectKind.Summon)
                    {
                        return effect.CharacterId;
                    }
                }

                return null;
            }
        }

        /// <summary>Résumé court des effets, pour les cartes de l'interface.</summary>
        /// <param name="characterName">Donne le nom affichable d'un personnage invoqué (son identifiant à défaut).</param>
        public string DescribeEffects(Func<string, string> characterName = null)
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
                        parts.Add("invoque " + (characterName != null ? characterName(effect.CharacterId) : effect.CharacterId));
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
                .Append('|').Append((int)UnitTargets);

            foreach (SpellEffect effect in Effects)
            {
                builder.Append('[').Append((int)effect.Kind).Append(',').Append(effect.Amount).Append(',')
                    .Append((int)effect.Targets);
                if (effect.CharacterId != null)
                {
                    builder.Append(',').Append(effect.CharacterId);
                }

                builder.Append(']');
            }

            builder.Append(';');
        }
    }
}
