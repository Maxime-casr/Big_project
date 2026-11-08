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

        /// <summary>3 joueurs contre 3 joueurs, une unité par joueur.</summary>
        public static MatchState CreateDemo3v3(MatchConfig config, SpellCatalog spells)
        {
            var map = new GridMap(12, 9);
            map.SetTile(new GridPosition(5, 4), TileType.Obstacle);
            map.SetTile(new GridPosition(6, 4), TileType.Obstacle);
            map.SetTile(new GridPosition(5, 2), TileType.Pit);
            map.SetTile(new GridPosition(6, 6), TileType.Pit);
            map.SetTile(new GridPosition(3, 7), TileType.Obstacle);
            map.SetTile(new GridPosition(8, 1), TileType.Obstacle);

            var state = new MatchState(map, config);
            int unitId = 1;
            for (int team = TeamBlue; team <= TeamRed; team++)
            {
                for (int slot = 0; slot < 3; slot++)
                {
                    var position = new GridPosition(team == TeamBlue ? 1 : 10, 2 + slot * 2);
                    string name = (team == TeamBlue ? "Bleu-" : "Rouge-") + (slot + 1);
                    state.AddUnit(new UnitState(
                        id: unitId,
                        playerId: unitId - 1,
                        teamId: team,
                        name: name,
                        position: position,
                        maxHealth: 30,
                        maxMana: 8,
                        movementPoints: 4,
                        spellIds: spells.AllIds));
                    unitId++;
                }
            }

            return state;
        }
    }
}
