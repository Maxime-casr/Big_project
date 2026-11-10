using System;
using System.Collections.Generic;
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
        private readonly float planningSeconds;
        private readonly float graceSeconds;

        /// <param name="planningSeconds">Durée de planification affichée au joueur, 0 pour illimitée.</param>
        /// <param name="graceSeconds">Marge de la résolution forcée, pour laisser le temps d'envoyer le plan final.</param>
        public LocalMatchSession(
            MatchState state,
            SpellCatalog spells,
            RoundResolver resolver,
            float planningSeconds = 0f,
            float graceSeconds = 1f)
        {
            Spells = spells;
            this.planningSeconds = planningSeconds;
            this.graceSeconds = graceSeconds;
            float timeout = planningSeconds > 0f ? planningSeconds + graceSeconds : 0f;
            match = new AuthoritativeMatch(state, resolver, timeout);
            match.RoundResolved += result => RoundResolved?.Invoke(result);
        }

        public MatchState State => match.State;
        public SpellCatalog Spells { get; }
        public int PlansReady => match.ReadyPlayerCount;
        public int PlansExpected => match.ExpectedPlayerCount;

        public float PlanningSecondsRemaining
        {
            get
            {
                if (planningSeconds <= 0f)
                {
                    return 0f;
                }

                return Math.Max(0f, Math.Min(planningSeconds, match.TimeRemainingSeconds - graceSeconds));
            }
        }

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

        // Hors ligne, tous les joueurs sont locaux : leurs brouillons sont déjà connus du client.
        public void SendDraft(IReadOnlyList<UnitPlan> units)
        {
        }

        public bool TryGetAllyDraft(int unitId, out IReadOnlyList<UnitOrder> orders)
        {
            orders = null;
            return false;
        }

        /// <summary>À appeler à chaque frame pour faire avancer le chrono de planification.</summary>
        public void Tick(float deltaSeconds)
        {
            match.Tick(deltaSeconds);
        }

        public RoundResult ResolveNow()
        {
            return match.ResolveNow();
        }
    }
}
