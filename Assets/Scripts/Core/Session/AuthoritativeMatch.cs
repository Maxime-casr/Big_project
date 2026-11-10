using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Units;

namespace BigProject.Core.Session
{
    /// <summary>
    /// Autorité d'un match : collecte les plans des joueurs, résout le tour quand tout le monde est
    /// prêt (ou quand le chrono expire) et publie le résultat. Utilisé tel quel par le serveur en ligne
    /// et par la session hors ligne : les règles de tour ne sont écrites qu'une fois.
    /// </summary>
    public sealed class AuthoritativeMatch
    {
        private readonly RoundResolver resolver;
        private readonly float planningTimeoutSeconds;
        private readonly SortedDictionary<int, TurnPlan> submittedPlans = new SortedDictionary<int, TurnPlan>();
        private readonly HashSet<int> disconnectedPlayers = new HashSet<int>();
        private float timeRemaining;

        /// <param name="planningTimeoutSeconds">Durée maximale de planification, 0 pour illimitée.</param>
        public AuthoritativeMatch(MatchState state, RoundResolver resolver, float planningTimeoutSeconds = 0f)
        {
            State = state;
            this.resolver = resolver;
            this.planningTimeoutSeconds = planningTimeoutSeconds;
            timeRemaining = planningTimeoutSeconds > 0f
                ? planningTimeoutSeconds + PlaybackTiming.InitialAllowanceSeconds
                : 0f;
        }

        public MatchState State { get; }

        /// <summary>Temps restant avant résolution forcée (inclut la marge d'animation), 0 si illimité.</summary>
        public float TimeRemainingSeconds => planningTimeoutSeconds > 0f ? Math.Max(0f, timeRemaining) : 0f;

        /// <summary>Joueurs ayant déjà validé leur tour.</summary>
        public int ReadyPlayerCount => submittedPlans.Count;

        public bool HasSubmitted(int playerId)
        {
            return submittedPlans.ContainsKey(playerId);
        }

        /// <summary>Joueurs encore attendus : en vie et connectés.</summary>
        public int ExpectedPlayerCount
        {
            get
            {
                int count = 0;
                foreach (int playerId in State.GetActivePlayerIds())
                {
                    if (!disconnectedPlayers.Contains(playerId))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

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
            ResolveIfEveryoneIsReady();
            return true;
        }

        /// <summary>Un joueur déconnecté n'est plus attendu : ses unités restent immobiles.</summary>
        public void MarkDisconnected(int playerId)
        {
            disconnectedPlayers.Add(playerId);
            ResolveIfEveryoneIsReady();
        }

        public void Tick(float deltaSeconds)
        {
            if (planningTimeoutSeconds <= 0f || State.Phase != MatchPhase.Planning)
            {
                return;
            }

            timeRemaining -= deltaSeconds;
            if (timeRemaining <= 0f)
            {
                ResolveNow();
            }
        }

        /// <summary>Résout immédiatement avec les plans reçus. Les joueurs sans plan restent immobiles.</summary>
        public RoundResult ResolveNow()
        {
            if (State.Phase != MatchPhase.Planning)
            {
                return null;
            }

            var plans = new List<TurnPlan>(submittedPlans.Values);
            submittedPlans.Clear();
            RoundResult result = resolver.Resolve(State, plans);
            timeRemaining = planningTimeoutSeconds > 0f
                ? planningTimeoutSeconds + PlaybackTiming.Estimate(result)
                : 0f;
            RoundResolved?.Invoke(result);
            return result;
        }

        private void ResolveIfEveryoneIsReady()
        {
            if (State.Phase != MatchPhase.Planning)
            {
                return;
            }

            bool anyConnectedPlayer = false;
            foreach (int playerId in State.GetActivePlayerIds())
            {
                if (disconnectedPlayers.Contains(playerId))
                {
                    continue;
                }

                anyConnectedPlayer = true;
                if (!submittedPlans.ContainsKey(playerId))
                {
                    return;
                }
            }

            if (anyConnectedPlayer)
            {
                ResolveNow();
            }
        }
    }
}
