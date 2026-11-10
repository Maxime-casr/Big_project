using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Resolution
{
    /// <summary>
    /// Résout un tour complet à partir des plans de tous les joueurs, action par action.
    ///
    /// À l'action N, chaque unité exécute sa N-ième action en même temps que les autres :
    /// - les déplacements avancent case par case, simultanément ;
    /// - les sorts sont lancés dès le début de l'action, en même temps que les déplacements ;
    /// - leur impact est calculé à la fin de l'action sur les positions d'arrivée : marcher hors de la zone
    ///   dans la même action évite les dégâts, viser la case où l'ennemi va arriver le touche.
    ///
    /// Une unité dont une action est perturbée (déplacement bloqué, sort impossible) est interrompue :
    /// toutes ses actions suivantes sont annulées. Un allié qui comptait traverser la case où elle s'est
    /// arrêtée est donc bloqué à son tour, et ainsi de suite.
    ///
    /// Le seul hasard (priorité entre deux unités qui visent la même case) vient du Random fourni : à graine
    /// égale, le résultat est identique.
    /// </summary>
    public sealed class RoundResolver
    {
        private readonly SpellCatalog spells;
        private readonly SpellResolver spellResolver;
        private readonly IVictoryCondition victoryCondition;
        private readonly Random random;

        public RoundResolver(SpellCatalog spells, IVictoryCondition victoryCondition, Random random = null)
        {
            this.spells = spells;
            this.victoryCondition = victoryCondition;
            this.random = random ?? new Random();
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
            var interrupted = new HashSet<int>();

            int stepCount = 0;
            foreach (KeyValuePair<int, IReadOnlyList<UnitOrder>> pair in ordersByUnit)
            {
                stepCount = Math.Max(stepCount, pair.Value.Count);
            }

            for (int step = 0; step < stepCount; step++)
            {
                ResolveStep(state, ordersByUnit, interrupted, step, result);

                if (victoryCondition.IsMatchOver(state, out _))
                {
                    break;
                }
            }

            FinishRound(state, result);
            return result;
        }

        private void ResolveStep(
            MatchState state,
            SortedDictionary<int, IReadOnlyList<UnitOrder>> ordersByUnit,
            HashSet<int> interrupted,
            int step,
            RoundResult result)
        {
            var moves = new List<PendingMove>();
            var casts = new List<PendingCast>();

            foreach (KeyValuePair<int, IReadOnlyList<UnitOrder>> pair in ordersByUnit)
            {
                UnitState unit = state.GetUnit(pair.Key);
                if (!unit.IsAlive || interrupted.Contains(unit.Id) || step >= pair.Value.Count)
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

            int firstBeat = result.Beats.Count;
            MovementResolver.Resolve(state, moves, result.Beats, random);
            int lastBeat = result.Beats.Count - 1;

            foreach (PendingMove move in moves)
            {
                if (move.Stopped)
                {
                    interrupted.Add(move.UnitId);
                }
            }

            if (casts.Count > 0)
            {
                // Les lanceurs ne bougent pas pendant leur action : le lancement peut donc être validé
                // avant la fin des déplacements, mais l'impact doit attendre les positions d'arrivée.
                var castBeat = new ResolutionBeat();
                var fizzled = new List<int>();
                List<ActiveCast> active = spellResolver.BeginCasts(state, casts, castBeat, fizzled);
                interrupted.UnionWith(fizzled);

                var impactBeat = new ResolutionBeat();
                spellResolver.ApplyImpacts(state, active, impactBeat);

                if (lastBeat < firstBeat)
                {
                    var single = new ResolutionBeat();
                    single.Events.AddRange(castBeat.Events);
                    single.Events.AddRange(impactBeat.Events);
                    result.Beats.Add(single);
                }
                else
                {
                    result.Beats[firstBeat].Events.InsertRange(0, castBeat.Events);
                    result.Beats[lastBeat].Events.AddRange(impactBeat.Events);
                }
            }

            for (int i = firstBeat; i < result.Beats.Count; i++)
            {
                result.Beats[i].StepIndex = step;
            }
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
