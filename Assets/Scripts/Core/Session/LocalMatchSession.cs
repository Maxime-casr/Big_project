using System;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Session hors ligne : joue le rôle du serveur en mémoire. Résout le tour dès que tous
    /// les joueurs encore en vie ont envoyé leur plan.
    /// </summary>
    public sealed class LocalMatchSession : IMatchSession
    {
        private readonly AuthoritativeMatch match;

        public LocalMatchSession(MatchState state, SpellCatalog spells, RoundResolver resolver)
        {
            Spells = spells;
            match = new AuthoritativeMatch(state, resolver);
            match.RoundResolved += result => RoundResolved?.Invoke(result);
        }

        public MatchState State => match.State;
        public SpellCatalog Spells { get; }
        public float PlanningSecondsRemaining => 0f;

        public event Action<RoundResult> RoundResolved;

        // Hors ligne, un plan invalide est signalé directement par TrySubmitPlan.
        public event Action<string> PlanRejected
        {
            add { }
            remove { }
        }

        public bool TrySubmitPlan(TurnPlan plan, out string error)
        {
            return match.TrySubmitPlan(plan, out error);
        }

        public RoundResult ResolveNow()
        {
            return match.ResolveNow();
        }
    }
}
