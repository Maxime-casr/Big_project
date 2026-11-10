using System.Collections.Generic;
using BigProject.Core.Grid;

namespace BigProject.Core.Match
{
    /// <summary>
    /// Une carte jouable : le terrain et, si la carte les définit, les cases de départ de chaque équipe.
    /// Sans cases de départ, la fabrique de match place les équipes de part et d'autre de la carte.
    /// </summary>
    public sealed class MapDefinition
    {
        public MapDefinition(GridMap grid)
        {
            Grid = grid;
        }

        public GridMap Grid { get; }
        public List<GridPosition> BlueSpawns { get; } = new List<GridPosition>();
        public List<GridPosition> RedSpawns { get; } = new List<GridPosition>();
    }
}
