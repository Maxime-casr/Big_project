using System.Collections.Generic;

namespace BigProject.Core.Orders
{
    /// <summary>Les actions ordonnées d'une unité pour un tour.</summary>
    public sealed class UnitPlan
    {
        public UnitPlan(int unitId, IEnumerable<UnitOrder> orders)
        {
            UnitId = unitId;
            Orders = new List<UnitOrder>(orders);
        }

        public int UnitId { get; }
        public IReadOnlyList<UnitOrder> Orders { get; }
    }

    /// <summary>Ce qu'un joueur envoie au serveur à la fin de sa phase de planification.</summary>
    public sealed class TurnPlan
    {
        public TurnPlan(int playerId, IEnumerable<UnitPlan> units)
        {
            PlayerId = playerId;
            Units = new List<UnitPlan>(units);
        }

        public int PlayerId { get; }
        public IReadOnlyList<UnitPlan> Units { get; }
    }
}
