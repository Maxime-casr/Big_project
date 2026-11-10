using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Resolution
{
    /// <summary>Fait observable survenu pendant la résolution. La vue les rejoue, elle ne les calcule pas.</summary>
    public abstract class GameEvent
    {
    }

    public sealed class UnitMovedEvent : GameEvent
    {
        public UnitMovedEvent(int unitId, GridPosition from, GridPosition to)
        {
            UnitId = unitId;
            From = from;
            To = to;
        }

        public int UnitId { get; }
        public GridPosition From { get; }
        public GridPosition To { get; }
    }

    /// <summary>Le déplacement s'arrête : case visée prise, contestée ou échange de places.</summary>
    public sealed class MoveBlockedEvent : GameEvent
    {
        public MoveBlockedEvent(int unitId, GridPosition at, GridPosition attempted)
        {
            UnitId = unitId;
            At = at;
            Attempted = attempted;
        }

        public int UnitId { get; }
        public GridPosition At { get; }
        public GridPosition Attempted { get; }
    }

    public sealed class SpellCastEvent : GameEvent
    {
        public SpellCastEvent(
            int casterId,
            string spellId,
            GridPosition target,
            IReadOnlyList<GridPosition> affectedCells,
            int targetUnitId = -1)
        {
            CasterId = casterId;
            SpellId = spellId;
            Target = target;
            AffectedCells = affectedCells;
            TargetUnitId = targetUnitId;
        }

        public int CasterId { get; }
        public string SpellId { get; }
        public GridPosition Target { get; }

        /// <summary>Zone visée dès le lancement (sorts sur une case). Vide pour un sort sur une unité.</summary>
        public IReadOnlyList<GridPosition> AffectedCells { get; }

        /// <summary>Unité verrouillée par le sort, ou -1.</summary>
        public int TargetUnitId { get; }
    }

    /// <summary>La zone réellement touchée à la fin de l'action (pour un sort sur une unité, autour de sa position finale).</summary>
    public sealed class SpellImpactEvent : GameEvent
    {
        public SpellImpactEvent(int casterId, string spellId, IReadOnlyList<GridPosition> cells)
        {
            CasterId = casterId;
            SpellId = spellId;
            Cells = cells;
        }

        public int CasterId { get; }
        public string SpellId { get; }
        public IReadOnlyList<GridPosition> Cells { get; }
    }

    /// <summary>Le lanceur disparaît d'une case et réapparaît sur une autre.</summary>
    public sealed class UnitTeleportedEvent : GameEvent
    {
        public UnitTeleportedEvent(int unitId, GridPosition from, GridPosition to)
        {
            UnitId = unitId;
            From = from;
            To = to;
        }

        public int UnitId { get; }
        public GridPosition From { get; }
        public GridPosition To { get; }
    }

    /// <summary>Une nouvelle unité apparaît. Ses caractéristiques sont dans l'état envoyé avec le résultat du tour.</summary>
    public sealed class UnitSummonedEvent : GameEvent
    {
        public UnitSummonedEvent(int unitId, int casterId, GridPosition position)
        {
            UnitId = unitId;
            CasterId = casterId;
            Position = position;
        }

        public int UnitId { get; }
        public int CasterId { get; }
        public GridPosition Position { get; }
    }

    /// <summary>Le sort n'a pas pu être lancé (lanceur bloqué hors de portée, par exemple).</summary>
    public sealed class SpellFizzledEvent : GameEvent
    {
        public SpellFizzledEvent(int casterId, string spellId, GridPosition target, string reason)
        {
            CasterId = casterId;
            SpellId = spellId;
            Target = target;
            Reason = reason;
        }

        public int CasterId { get; }
        public string SpellId { get; }
        public GridPosition Target { get; }
        public string Reason { get; }
    }

    public sealed class UnitDamagedEvent : GameEvent
    {
        public UnitDamagedEvent(int unitId, int amount, int healthAfter)
        {
            UnitId = unitId;
            Amount = amount;
            HealthAfter = healthAfter;
        }

        public int UnitId { get; }
        public int Amount { get; }
        public int HealthAfter { get; }
    }

    public sealed class UnitHealedEvent : GameEvent
    {
        public UnitHealedEvent(int unitId, int amount, int healthAfter)
        {
            UnitId = unitId;
            Amount = amount;
            HealthAfter = healthAfter;
        }

        public int UnitId { get; }
        public int Amount { get; }
        public int HealthAfter { get; }
    }

    public sealed class UnitDiedEvent : GameEvent
    {
        public UnitDiedEvent(int unitId)
        {
            UnitId = unitId;
        }

        public int UnitId { get; }
    }
}
