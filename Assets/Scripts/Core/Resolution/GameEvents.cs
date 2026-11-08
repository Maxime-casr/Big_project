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
        public SpellCastEvent(int casterId, string spellId, GridPosition target, IReadOnlyList<GridPosition> affectedCells)
        {
            CasterId = casterId;
            SpellId = spellId;
            Target = target;
            AffectedCells = affectedCells;
        }

        public int CasterId { get; }
        public string SpellId { get; }
        public GridPosition Target { get; }
        public IReadOnlyList<GridPosition> AffectedCells { get; }
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
