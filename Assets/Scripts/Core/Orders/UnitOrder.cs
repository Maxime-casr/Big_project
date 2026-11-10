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
        /// <summary>Aucune unité visée (sort sur une case, ou sur soi).</summary>
        public const int NoUnit = -1;

        /// <summary>Sort sur une case, ou sur soi.</summary>
        public CastOrder(string spellId, GridPosition target)
            : this(spellId, target, NoUnit)
        {
        }

        /// <param name="target">
        /// La case visée. Pour un sort sur une unité, c'est seulement la position de la cible au moment de la
        /// planification (pour l'affichage) : seul l'identifiant de l'unité compte à la résolution.
        /// </param>
        /// <param name="targetUnitId">L'unité visée, ou NoUnit.</param>
        public CastOrder(string spellId, GridPosition target, int targetUnitId)
        {
            SpellId = spellId;
            Target = target;
            TargetUnitId = targetUnitId;
        }

        public string SpellId { get; }
        public GridPosition Target { get; }
        public int TargetUnitId { get; }
    }
}
