using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Resolution
{
    /// <summary>
    /// Résout un tour complet à partir des plans de tous les joueurs.
    ///
    /// Le tour est découpé en étapes : à l'étape N, chaque unité exécute sa N-ième action en même temps
    /// que les autres. Dans une étape, les déplacements se font d'abord (case par case, simultanément),
    /// puis tous les sorts partent ensemble sur les positions obtenues : viser la case où l'ennemi va
    /// arriver est donc possible. Le résultat est une suite de "beats" que le client rejoue.
    ///
    /// Déterministe : aucun aléatoire, unités toujours parcourues par identifiant croissant.
    /// </summary>
    public sealed class RoundResolver
    {
        private readonly SpellCatalog spells;
        private readonly SpellResolver spellResolver;
        private readonly IVictoryCondition victoryCondition;

        public RoundResolver(SpellCatalog spells, IVictoryCondition victoryCondition)
        {
            this.spells = spells;
            this.victoryCondition = victoryCondition;
            spellResolver = new SpellResolver(spells);
        }

        public RoundResult Resolve(MatchState state, IReadOnlyList<TurnPlan> plans)
        {
            if (state.Phase != MatchPhase.Planning)
            {
                throw new InvalidOperationException("Le match n'est pas en phase de planification.");
            }

            state.Phase = MatchPhase.Resolving;
            var result = new RoundResult(state.RoundNumber);
            SortedDictionary<int, IReadOnlyList<UnitOrder>> ordersByUnit = CollectValidOrders(state, plans, result);

            int stepCount = 0;
            foreach (KeyValuePair<int, IReadOnlyList<UnitOrder>> pair in ordersByUnit)
            {
                stepCount = Math.Max(stepCount, pair.Value.Count);
            }

            for (int step = 0; step < stepCount; step++)
            {
                var moves = new List<PendingMove>();
                var casts = new List<PendingCast>();

                foreach (KeyValuePair<int, IReadOnlyList<UnitOrder>> pair in ordersByUnit)
                {
                    UnitState unit = state.GetUnit(pair.Key);
                    if (!unit.IsAlive || step >= pair.Value.Count)
                    {
                        continue;
                    }

                    UnitOrder order = pair.Value[step];
                    if (order is MoveOrder move)
                    {
                        moves.Add(new PendingMove(unit.Id, move.Path));
                    }
                    else if (order is CastOrder cast)
                    {
                        casts.Add(new PendingCast(unit.Id, cast));
                    }
                }

                MovementResolver.Resolve(state, moves, result.Beats);
                if (casts.Count > 0)
                {
                    result.Beats.Add(spellResolver.Resolve(state, casts));
                }

                if (victoryCondition.IsMatchOver(state, out _))
                {
                    break;
                }
            }

            FinishRound(state, result);
            return result;
        }

        private SortedDictionary<int, IReadOnlyList<UnitOrder>> CollectValidOrders(
            MatchState state,
            IReadOnlyList<TurnPlan> plans,
            RoundResult result)
        {
            var accepted = new SortedDictionary<int, IReadOnlyList<UnitOrder>>();

            foreach (TurnPlan plan in plans)
            {
                foreach (UnitPlan unitPlan in plan.Units)
                {
                    UnitState unit = state.GetUnit(unitPlan.UnitId);
                    string reason = null;

                    if (unit == null)
                    {
                        reason = "Unité inconnue.";
                    }
                    else if (unit.PlayerId != plan.PlayerId)
                    {
                        reason = "Cette unité n'appartient pas au joueur.";
                    }
                    else if (accepted.ContainsKey(unit.Id))
                    {
                        reason = "Plan déjà reçu pour cette unité.";
                    }
                    else
                    {
                        PlanProjection projection = PlanValidator.Project(state, spells, unit, unitPlan.Orders);
                        reason = projection.Error;
                    }

                    if (reason != null)
                    {
                        result.RejectedPlans.Add(new RejectedPlan(unitPlan.UnitId, reason));
                    }
                    else
                    {
                        accepted.Add(unitPlan.UnitId, unitPlan.Orders);
                    }
                }
            }

            return accepted;
        }

        private void FinishRound(MatchState state, RoundResult result)
        {
            foreach (UnitState unit in state.Units)
            {
                if (unit.IsAlive)
                {
                    unit.RestoreMana(state.Config.ManaRegenPerRound);
                }
            }

            if (victoryCondition.IsMatchOver(state, out int winnerTeamId))
            {
                state.Phase = MatchPhase.Finished;
                state.WinnerTeamId = winnerTeamId;
                result.MatchEnded = true;
                result.WinnerTeamId = winnerTeamId;
                return;
            }

            state.RoundNumber++;
            state.Phase = MatchPhase.Planning;
        }
    }
}
