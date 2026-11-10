using System.Collections.Generic;
using BigProject.Core.Units;

namespace BigProject.Core.Match
{
    /// <summary>Condition de fin de match. Ajoutez-en une par mode (3v3, battle royale...).</summary>
    public interface IVictoryCondition
    {
        /// <summary>Retourne true si le match est terminé. winnerTeamId vaut NoTeam en cas d'égalité.</summary>
        bool IsMatchOver(MatchState state, out int winnerTeamId);
    }

    /// <summary>Le match s'arrête quand il reste au plus une équipe en vie.</summary>
    public sealed class LastTeamStandingCondition : IVictoryCondition
    {
        public bool IsMatchOver(MatchState state, out int winnerTeamId)
        {
            var aliveTeams = new HashSet<int>();
            foreach (UnitState unit in state.Units)
            {
                // Les invocations ne comptent pas : une équipe dont les personnages sont tous vaincus a perdu.
                if (unit.IsAlive && !unit.IsSummon)
                {
                    aliveTeams.Add(unit.TeamId);
                }
            }

            if (aliveTeams.Count > 1)
            {
                winnerTeamId = MatchState.NoTeam;
                return false;
            }

            winnerTeamId = MatchState.NoTeam;
            foreach (int team in aliveTeams)
            {
                winnerTeamId = team;
            }

            return true;
        }
    }
}
