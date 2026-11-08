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
            return CreateTeamMatch(config, spells, 3);
        }

        /// <summary>
        /// N joueurs contre N joueurs, une unité par joueur. Les joueurs 0..N-1 forment l'équipe bleue,
        /// les suivants l'équipe rouge ; l'identifiant d'unité est l'identifiant de joueur + 1.
        /// </summary>
        public static MatchState CreateTeamMatch(MatchConfig config, SpellCatalog spells, int playersPerTeam)
        {
            if (playersPerTeam < 1 || playersPerTeam > 4)
            {
                throw new System.ArgumentOutOfRangeException(nameof(playersPerTeam), "Entre 1 et 4 joueurs par équipe.");
            }

            var map = new GridMap(12, 9);
            map.SetTile(new GridPosition(5, 4), TileType.Obstacle);
            map.SetTile(new GridPosition(6, 4), TileType.Obstacle);
            map.SetTile(new GridPosition(5, 2), TileType.Pit);
            map.SetTile(new GridPosition(6, 6), TileType.Pit);
            map.SetTile(new GridPosition(3, 7), TileType.Obstacle);
            map.SetTile(new GridPosition(8, 1), TileType.Obstacle);

            var state = new MatchState(map, config);
            int firstRow = (map.Height - 1 - (playersPerTeam - 1) * 2) / 2;
            int unitId = 1;
            for (int team = TeamBlue; team <= TeamRed; team++)
            {
                for (int slot = 0; slot < playersPerTeam; slot++)
                {
                    var position = new GridPosition(team == TeamBlue ? 1 : 10, firstRow + slot * 2);
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
