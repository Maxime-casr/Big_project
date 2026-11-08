using System.Collections.Generic;
using BigProject.Core.Match;

namespace BigProject.Core.Resolution
{
    /// <summary>Un instant de la résolution : tous ses événements se produisent en même temps.</summary>
    public sealed class ResolutionBeat
    {
        public List<GameEvent> Events { get; } = new List<GameEvent>();
    }

    public sealed class RejectedPlan
    {
        public RejectedPlan(int unitId, string reason)
        {
            UnitId = unitId;
            Reason = reason;
        }

        public int UnitId { get; }
        public string Reason { get; }
    }

    /// <summary>Tout ce que les clients ont besoin de rejouer pour un tour.</summary>
    public sealed class RoundResult
    {
        public RoundResult(int roundNumber)
        {
            RoundNumber = roundNumber;
        }

        public int RoundNumber { get; }
        public List<ResolutionBeat> Beats { get; } = new List<ResolutionBeat>();
        public List<RejectedPlan> RejectedPlans { get; } = new List<RejectedPlan>();
        public bool MatchEnded { get; set; }
        public int WinnerTeamId { get; set; } = MatchState.NoTeam;
    }
}
