using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Orders
{
    /// <summary>Une action planifiée pour une unité. L'ordre dans la liste est l'ordre d'exécution.</summary>
    public abstract class UnitOrder
    {
    }

    /// <summary>Déplacement case par case. Le chemin exclut la case de départ.</summary>
    public sealed class MoveOrder : UnitOrder
    {
        public MoveOrder(IEnumerable<GridPosition> path)
        {
            Path = new List<GridPosition>(path);
        }

        public IReadOnlyList<GridPosition> Path { get; }
    }

    public sealed class CastOrder : UnitOrder
    {
        public CastOrder(string spellId, GridPosition target)
        {
            SpellId = spellId;
            Target = target;
        }

        public string SpellId { get; }
        public GridPosition Target { get; }
    }
}
