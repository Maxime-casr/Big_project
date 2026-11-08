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
        private readonly RoundResolver resolver;
        private readonly SortedDictionary<int, TurnPlan> submittedPlans = new SortedDictionary<int, TurnPlan>();

        public LocalMatchSession(MatchState state, SpellCatalog spells, RoundResolver resolver)
        {
            State = state;
            Spells = spells;
            this.resolver = resolver;
        }

        public MatchState State { get; }
        public SpellCatalog Spells { get; }

        public event Action<RoundResult> RoundResolved;

        public bool TrySubmitPlan(TurnPlan plan, out string error)
        {
            if (plan == null)
            {
                error = "Plan vide.";
                return false;
            }

            if (State.Phase != MatchPhase.Planning)
            {
                error = "Le match n'accepte pas de plan pour le moment.";
                return false;
            }

            if (!State.GetActivePlayerIds().Contains(plan.PlayerId))
            {
                error = "Ce joueur n'a plus d'unité en vie.";
                return false;
            }

            if (submittedPlans.ContainsKey(plan.PlayerId))
            {
                error = "Ce joueur a déjà validé son tour.";
                return false;
            }

            submittedPlans.Add(plan.PlayerId, plan);
            error = null;

            if (submittedPlans.Count >= State.GetActivePlayerIds().Count)
            {
                ResolveNow();
            }

            return true;
        }

        /// <summary>Force la résolution (fin du chrono, par exemple). Les joueurs sans plan restent immobiles.</summary>
        public RoundResult ResolveNow()
        {
            var plans = new List<TurnPlan>(submittedPlans.Values);
            submittedPlans.Clear();
            RoundResult result = resolver.Resolve(State, plans);
            RoundResolved?.Invoke(result);
            return result;
        }
    }
}
