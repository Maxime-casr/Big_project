using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Match
{
    /// <summary>Construit l'état initial d'un match. Un mode de jeu = une méthode ici.</summary>
    public static class MatchFactory
    {
        public const int TeamBlue = 0;
        public const int TeamRed = 1;

        /// <summary>3 joueurs contre 3 joueurs sur la carte par défaut, une unité par joueur.</summary>
        public static MatchState CreateDemo3v3(MatchConfig config, SpellCatalog spells)
        {
            return CreateTeamMatch(config, spells, 3);
        }

        /// <summary>Carte codée en dur, utilisée quand aucune carte n'est fournie.</summary>
        public static MapDefinition CreateDefaultMap()
        {
            var grid = new GridMap(12, 9);
            grid.SetTile(new GridPosition(5, 4), TileType.Obstacle);
            grid.SetTile(new GridPosition(6, 4), TileType.Obstacle);
            grid.SetTile(new GridPosition(5, 2), TileType.Pit);
            grid.SetTile(new GridPosition(6, 6), TileType.Pit);
            grid.SetTile(new GridPosition(3, 7), TileType.Obstacle);
            grid.SetTile(new GridPosition(8, 1), TileType.Obstacle);
            return new MapDefinition(grid);
        }

        /// <summary>
        /// N joueurs contre N joueurs, une unité par joueur. Les joueurs 0..N-1 forment l'équipe bleue,
        /// les suivants l'équipe rouge ; l'identifiant d'unité est l'identifiant de joueur + 1.
        /// </summary>
        /// <param name="map">Carte à utiliser ; null pour la carte par défaut.</param>
        public static MatchState CreateTeamMatch(
            MatchConfig config,
            SpellCatalog spells,
            int playersPerTeam,
            MapDefinition map = null)
        {
            if (playersPerTeam < 1 || playersPerTeam > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(playersPerTeam), "Entre 1 et 4 joueurs par équipe.");
            }

            map = map ?? CreateDefaultMap();
            var state = new MatchState(map.Grid, config);
            var used = new HashSet<GridPosition>();

            List<GridPosition> blue = PickSpawns(map.Grid, map.BlueSpawns, playersPerTeam, true, used);
            List<GridPosition> red = PickSpawns(map.Grid, map.RedSpawns, playersPerTeam, false, used);

            int unitId = 1;
            for (int team = TeamBlue; team <= TeamRed; team++)
            {
                List<GridPosition> spawns = team == TeamBlue ? blue : red;
                for (int slot = 0; slot < playersPerTeam; slot++)
                {
                    state.AddUnit(new UnitState(
                        id: unitId,
                        playerId: unitId - 1,
                        teamId: team,
                        name: (team == TeamBlue ? "Bleu-" : "Rouge-") + (slot + 1),
                        position: spawns[slot],
                        maxHealth: 30,
                        maxMana: 8,
                        movementPoints: 4,
                        spellIds: spells.StartingIds));
                    unitId++;
                }
            }

            return state;
        }

        /// <summary>
        /// Cases de départ d'une équipe : celles de la carte si elles sont assez nombreuses et valides,
        /// sinon une colonne près du bord gauche (bleus) ou droit (rouges), décalée vers une case libre
        /// si besoin. Ne dépend jamais de la taille de la carte.
        /// </summary>
        private static List<GridPosition> PickSpawns(
            GridMap grid,
            List<GridPosition> wanted,
            int count,
            bool leftSide,
            HashSet<GridPosition> used)
        {
            var result = new List<GridPosition>(count);

            foreach (GridPosition spawn in wanted)
            {
                if (result.Count < count && grid.IsWalkable(spawn) && used.Add(spawn))
                {
                    result.Add(spawn);
                }
            }

            int column = leftSide ? 1 : grid.Width - 2;
            int firstRow = (grid.Height - 1 - (count - 1) * 2) / 2;
            for (int slot = 0; result.Count < count; slot++)
            {
                var desired = new GridPosition(column, firstRow + slot * 2);
                GridPosition? cell = NearestFreeWalkable(grid, desired, used);
                if (!cell.HasValue)
                {
                    throw new InvalidOperationException("La carte n'a pas assez de cases jouables pour placer les équipes.");
                }

                used.Add(cell.Value);
                result.Add(cell.Value);
            }

            return result;
        }

        private static GridPosition? NearestFreeWalkable(GridMap grid, GridPosition target, HashSet<GridPosition> used)
        {
            GridPosition? best = null;
            int bestDistance = int.MaxValue;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var candidate = new GridPosition(x, y);
                    if (!grid.IsWalkable(candidate) || used.Contains(candidate))
                    {
                        continue;
                    }

                    int distance = candidate.DistanceTo(target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }

            return best;
        }
    }
}
