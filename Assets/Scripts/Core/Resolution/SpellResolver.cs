using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Resolution
{
    public sealed class PendingCast
    {
        public PendingCast(int unitId, CastOrder order)
        {
            UnitId = unitId;
            Order = order;
        }

        public int UnitId { get; }
        public CastOrder Order { get; }
    }

    /// <summary>Sort lancé avec succès dont les effets restent à appliquer.</summary>
    public sealed class ActiveCast
    {
        public ActiveCast(UnitState caster, SpellDefinition spell, IReadOnlyList<GridPosition> cells)
        {
            Caster = caster;
            Spell = spell;
            Cells = cells;
        }

        public UnitState Caster { get; }
        public SpellDefinition Spell { get; }
        public IReadOnlyList<GridPosition> Cells { get; }
    }

    /// <summary>
    /// Résout les sorts d'une action en deux temps :
    /// 1. le lancement (validation, mana, zone) a lieu au début de l'action, en même temps que les déplacements ;
    /// 2. l'impact est calculé à la fin de l'action, sur les positions d'arrivée : quitter la zone en marchant
    ///    dans la même action évite les dégâts.
    /// Tous les effets d'une même action sont appliqués ensemble (un lanceur tué dans l'action lance quand même).
    /// </summary>
    public sealed class SpellResolver
    {
        private readonly SpellCatalog spells;

        public SpellResolver(SpellCatalog spells)
        {
            this.spells = spells;
        }

        /// <summary>Valide et lance les sorts. Les lanceurs dont le sort échoue sont ajoutés à fizzledCasters.</summary>
        public List<ActiveCast> BeginCasts(
            MatchState state,
            IReadOnlyList<PendingCast> casts,
            ResolutionBeat beat,
            List<int> fizzledCasters)
        {
            var active = new List<ActiveCast>();

            foreach (PendingCast pending in casts)
            {
                UnitState caster = state.GetUnit(pending.UnitId);
                CastOrder order = pending.Order;
                SpellDefinition spell = spells.Get(order.SpellId);

                string failure = null;
                if (spell == null)
                {
                    failure = "Sort inconnu.";
                }
                else if (caster.Mana < spell.ManaCost)
                {
                    failure = "Mana insuffisant.";
                }
                else if (!SpellTargeting.CanTarget(state.Map, spell, caster.Position, order.Target, out string reason))
                {
                    failure = reason;
                }

                if (failure != null)
                {
                    beat.Events.Add(new SpellFizzledEvent(caster.Id, order.SpellId, order.Target, failure));
                    fizzledCasters.Add(caster.Id);
                    continue;
                }

                caster.SpendMana(spell.ManaCost);
                List<GridPosition> cells = SpellArea.GetCells(state.Map, spell.AreaShape, spell.AreaRadius, order.Target);
                beat.Events.Add(new SpellCastEvent(caster.Id, spell.Id, order.Target, cells));
                active.Add(new ActiveCast(caster, spell, cells));
            }

            return active;
        }

        /// <summary>Applique les effets de tous les sorts lancés, sur les positions actuelles.</summary>
        public void ApplyImpacts(MatchState state, List<ActiveCast> active, ResolutionBeat beat)
        {
            var impacts = new SortedDictionary<int, Impact>();

            foreach (ActiveCast cast in active)
            {
                foreach (SpellEffect effect in cast.Spell.Effects)
                {
                    foreach (GridPosition cell in cast.Cells)
                    {
                        UnitState victim = state.GetUnitAt(cell);
                        if (victim == null || !Matches(effect.Targets, cast.Caster, victim))
                        {
                            continue;
                        }

                        if (!impacts.TryGetValue(victim.Id, out Impact impact))
                        {
                            impact = new Impact();
                            impacts.Add(victim.Id, impact);
                        }

                        if (effect.Kind == SpellEffectKind.Damage)
                        {
                            impact.Damage += effect.Amount;
                        }
                        else
                        {
                            impact.Heal += effect.Amount;
                        }
                    }
                }
            }

            foreach (KeyValuePair<int, Impact> pair in impacts)
            {
                UnitState unit = state.GetUnit(pair.Key);
                unit.ChangeHealth(pair.Value.Heal - pair.Value.Damage);

                if (pair.Value.Damage > 0)
                {
                    beat.Events.Add(new UnitDamagedEvent(unit.Id, pair.Value.Damage, unit.Health));
                }

                if (pair.Value.Heal > 0)
                {
                    beat.Events.Add(new UnitHealedEvent(unit.Id, pair.Value.Heal, unit.Health));
                }

                if (!unit.IsAlive)
                {
                    beat.Events.Add(new UnitDiedEvent(unit.Id));
                }
            }
        }

        private static bool Matches(TargetFilter filter, UnitState caster, UnitState victim)
        {
            if (victim.Id == caster.Id)
            {
                return (filter & TargetFilter.Self) != 0;
            }

            return victim.TeamId == caster.TeamId
                ? (filter & TargetFilter.Allies) != 0
                : (filter & TargetFilter.Enemies) != 0;
        }

        private sealed class Impact
        {
            public int Damage;
            public int Heal;
        }
    }
}
