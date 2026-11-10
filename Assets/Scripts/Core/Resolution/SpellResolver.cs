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

    /// <summary>Sort lancé avec succès dont l'impact reste à appliquer.</summary>
    public sealed class ActiveCast
    {
        public ActiveCast(UnitState caster, SpellDefinition spell, int lockedUnitId, IReadOnlyList<GridPosition> cells)
        {
            Caster = caster;
            Spell = spell;
            LockedUnitId = lockedUnitId;
            Cells = cells;
        }

        public UnitState Caster { get; }
        public SpellDefinition Spell { get; }

        /// <summary>
        /// Unité que le sort suit jusqu'à l'impact (sort sur une unité, ou sur soi), ou CastOrder.NoUnit pour une
        /// zone fixe. Avec une unité, la zone se calcule autour de sa position à l'impact.
        /// </summary>
        public int LockedUnitId { get; }

        /// <summary>Zone fixe (sorts sur une case).</summary>
        public IReadOnlyList<GridPosition> Cells { get; }
    }

    /// <summary>Ce que le lancement d'une série de sorts a produit.</summary>
    public sealed class CastBatch
    {
        public List<ActiveCast> Active { get; } = new List<ActiveCast>();

        /// <summary>Téléportations et invocations, à résoudre avec les déplacements.</summary>
        public List<PendingMove> Placements { get; } = new List<PendingMove>();
    }

    /// <summary>
    /// Résout les sorts d'une action en deux temps :
    /// 1. le lancement a lieu au début de l'action, sur les positions de ce moment : sort valide ou non, mana
    ///    dépensé, cible verrouillée ; les téléportations et invocations partent avec les déplacements ;
    /// 2. l'impact (dégâts, soins) est calculé à la fin de l'action : un sort sur une case frappe ce qui s'y
    ///    trouve alors (en sortir pendant l'action évite les dégâts), un sort sur une unité la frappe où qu'elle soit.
    /// Tous les effets d'une même action sont appliqués ensemble (un lanceur tué dans l'action lance quand même).
    /// </summary>
    public sealed class SpellResolver
    {
        private readonly SpellCatalog spells;

        public SpellResolver(SpellCatalog spells)
        {
            this.spells = spells;
        }

        /// <summary>
        /// Valide et lance les sorts. Les lanceurs dont le sort échoue sont ajoutés à fizzledCasters : leurs
        /// actions suivantes seront annulées.
        /// </summary>
        public CastBatch BeginCasts(
            MatchState state,
            IReadOnlyList<PendingCast> casts,
            ResolutionBeat beat,
            List<int> fizzledCasters)
        {
            var batch = new CastBatch();
            int nextUnitId = state.NextUnitId();
            var pendingSummons = new Dictionary<int, int>();

            foreach (PendingCast pending in casts)
            {
                UnitState caster = state.GetUnit(pending.UnitId);
                CastOrder order = pending.Order;
                SpellDefinition spell = spells.Get(order.SpellId);

                string failure = Validate(state, caster, spell, order, pendingSummons);
                if (failure != null)
                {
                    beat.Events.Add(new SpellFizzledEvent(caster.Id, order.SpellId, order.Target, failure));
                    fizzledCasters.Add(caster.Id);
                    continue;
                }

                caster.SpendMana(spell.ManaCost);

                int lockedUnit = CastOrder.NoUnit;
                var cells = new List<GridPosition>();
                switch (spell.Targeting)
                {
                    case TargetKind.Unit:
                        lockedUnit = order.TargetUnitId;
                        break;
                    case TargetKind.Self:
                        lockedUnit = caster.Id;
                        break;
                    default:
                        cells = SpellArea.GetCells(state.Map, spell.AreaShape, spell.AreaRadius, order.Target);
                        break;
                }

                beat.Events.Add(new SpellCastEvent(caster.Id, spell.Id, order.Target, cells, lockedUnit));

                foreach (SpellEffect effect in spell.Effects)
                {
                    if (effect.Kind == SpellEffectKind.Teleport)
                    {
                        batch.Placements.Add(PendingMove.Teleport(caster.Id, order.Target, spell.Id));
                    }
                    else if (effect.Kind == SpellEffectKind.Summon)
                    {
                        var summoned = new UnitState(
                            nextUnitId++,
                            caster.PlayerId,
                            caster.TeamId,
                            effect.Summon.Name + " de " + caster.Name,
                            order.Target,
                            effect.Summon.MaxHealth,
                            effect.Summon.MaxMana,
                            effect.Summon.MovementPoints,
                            effect.Summon.SpellIds,
                            isSummon: true,
                            ownerUnitId: caster.Id);
                        batch.Placements.Add(PendingMove.Summon(caster.Id, summoned, spell.Id));
                        pendingSummons[caster.PlayerId] = (pendingSummons.TryGetValue(caster.PlayerId, out int n) ? n : 0) + 1;
                    }
                }

                batch.Active.Add(new ActiveCast(caster, spell, lockedUnit, cells));
            }

            return batch;
        }

        /// <summary>Retourne la raison de l'échec, ou null si le sort peut être lancé.</summary>
        private string Validate(
            MatchState state,
            UnitState caster,
            SpellDefinition spell,
            CastOrder order,
            Dictionary<int, int> pendingSummons)
        {
            if (spell == null)
            {
                return "Sort inconnu.";
            }

            if (caster.Mana < spell.ManaCost)
            {
                return "Mana insuffisant.";
            }

            string reason;
            switch (spell.Targeting)
            {
                case TargetKind.Unit:
                    // La portée se juge maintenant, sur les positions du début de l'action : c'est le moment du lancement.
                    UnitState target = state.GetUnit(order.TargetUnitId);
                    if (!SpellTargeting.CanTargetUnit(state.Map, spell, caster.Position, caster, target, out reason))
                    {
                        return reason;
                    }

                    break;

                case TargetKind.Cell:
                    if (!SpellTargeting.CanTarget(state.Map, spell, caster.Position, order.Target, out reason))
                    {
                        return reason;
                    }

                    if ((spell.HasEffect(SpellEffectKind.Teleport) || spell.HasEffect(SpellEffectKind.Summon)) &&
                        !state.Map.IsWalkable(order.Target))
                    {
                        return "On ne peut pas se placer sur cette case.";
                    }

                    break;
            }

            if (spell.HasEffect(SpellEffectKind.Summon))
            {
                pendingSummons.TryGetValue(caster.PlayerId, out int already);
                if (state.CountLivingSummons(caster.PlayerId) + already >= state.Config.MaxSummonsPerPlayer)
                {
                    return "Limite d'invocations atteinte.";
                }
            }

            return null;
        }

        /// <summary>Applique les dégâts et les soins de tous les sorts lancés, sur les positions actuelles.</summary>
        public void ApplyImpacts(MatchState state, List<ActiveCast> active, ResolutionBeat beat)
        {
            var impacts = new SortedDictionary<int, Impact>();

            foreach (ActiveCast cast in active)
            {
                if (!HasImpactEffect(cast.Spell))
                {
                    continue;
                }

                IReadOnlyList<GridPosition> cells = cast.Cells;
                if (cast.LockedUnitId != CastOrder.NoUnit)
                {
                    UnitState locked = state.GetUnit(cast.LockedUnitId);
                    if (locked == null || !locked.IsAlive)
                    {
                        continue;
                    }

                    cells = SpellArea.GetCells(state.Map, cast.Spell.AreaShape, cast.Spell.AreaRadius, locked.Position);
                }

                beat.Events.Add(new SpellImpactEvent(cast.Caster.Id, cast.Spell.Id, cells));

                foreach (SpellEffect effect in cast.Spell.Effects)
                {
                    if (!effect.IsImpact)
                    {
                        continue;
                    }

                    foreach (GridPosition cell in cells)
                    {
                        UnitState victim = state.GetUnitAt(cell);
                        if (victim == null || !SpellTargeting.Matches(effect.Targets, cast.Caster, victim))
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

        private static bool HasImpactEffect(SpellDefinition spell)
        {
            foreach (SpellEffect effect in spell.Effects)
            {
                if (effect.IsImpact)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class Impact
        {
            public int Damage;
            public int Heal;
        }
    }
}
