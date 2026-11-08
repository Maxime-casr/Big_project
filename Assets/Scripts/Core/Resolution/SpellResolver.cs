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

    /// <summary>
    /// Résout tous les sorts d'une étape en même temps : les effets sont d'abord calculés
    /// sur l'état courant, puis appliqués ensemble (un lanceur tué dans l'étape lance quand même son sort).
    /// </summary>
    public sealed class SpellResolver
    {
        private readonly SpellCatalog spells;

        public SpellResolver(SpellCatalog spells)
        {
            this.spells = spells;
        }

        public ResolutionBeat Resolve(MatchState state, IReadOnlyList<PendingCast> casts)
        {
            var beat = new ResolutionBeat();
            var impacts = new SortedDictionary<int, Impact>();

            foreach (PendingCast pending in casts)
            {
                UnitState caster = state.GetUnit(pending.UnitId);
                CastOrder order = pending.Order;
                SpellDefinition spell = spells.Get(order.SpellId);

                if (spell == null)
                {
                    beat.Events.Add(new SpellFizzledEvent(caster.Id, order.SpellId, order.Target, "Sort inconnu."));
                    continue;
                }

                if (caster.Mana < spell.ManaCost)
                {
                    beat.Events.Add(new SpellFizzledEvent(caster.Id, spell.Id, order.Target, "Mana insuffisant."));
                    continue;
                }

                if (!SpellTargeting.CanTarget(state.Map, spell, caster.Position, order.Target, out string reason))
                {
                    beat.Events.Add(new SpellFizzledEvent(caster.Id, spell.Id, order.Target, reason));
                    continue;
                }

                caster.SpendMana(spell.ManaCost);
                List<GridPosition> cells = SpellArea.GetCells(state.Map, spell.AreaShape, spell.AreaRadius, order.Target);
                beat.Events.Add(new SpellCastEvent(caster.Id, spell.Id, order.Target, cells));

                foreach (SpellEffect effect in spell.Effects)
                {
                    foreach (GridPosition cell in cells)
                    {
                        UnitState victim = state.GetUnitAt(cell);
                        if (victim == null || !Matches(effect.Targets, caster, victim))
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

            return beat;
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
