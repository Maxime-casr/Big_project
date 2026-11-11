using System;
using System.Collections.Generic;
using BigProject.Core.Characters;
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
    /// 1. les sorts sont lancés (validés sur les positions du début de l'action, mana dépensé, cibles verrouillées) ;
    /// 2. les déplacements avancent case par case ; les téléportations et invocations arrivent au premier instant,
    ///    avec les mêmes règles de collision (initiative tirée au sort si plusieurs visent la même case) ;
    /// 3. les impacts sont calculés à la fin de l'action : marcher hors de la zone d'un sort sur une case évite les
    ///    dégâts ; un sort sur une unité la touche où qu'elle soit allée.
    ///
    /// Une unité dont une action est perturbée (déplacement bloqué, sort impossible, cible hors de portée) est
    /// interrompue : toutes ses actions suivantes sont annulées. Un allié qui comptait traverser la case où elle
    /// s'est arrêtée est donc bloqué à son tour, et ainsi de suite.
    ///
    /// Le seul hasard (initiative en cas de collision) vient du Random fourni : à graine égale, le résultat est identique.
    /// </summary>
    public sealed class RoundResolver
    {
        private readonly SpellCatalog spells;
        private readonly SpellResolver spellResolver;
        private readonly IVictoryCondition victoryCondition;
        private readonly Random random;

        /// <param name="characters">Nécessaire aux invocations (le personnage invoqué) ; par défaut, les personnages d'exemple.</param>
        public RoundResolver(
            SpellCatalog spells,
            IVictoryCondition victoryCondition,
            Random random = null,
            CharacterCatalog characters = null)
        {
            this.spells = spells;
            this.victoryCondition = victoryCondition;
            this.random = random ?? new Random();
            spellResolver = new SpellResolver(spells, characters ?? CharacterCatalog.CreateDefault());
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

            // 1. Lancement des sorts, sur les positions du début de l'action.
            var launchEvents = new ResolutionBeat();
            CastBatch batch = new CastBatch();
            if (casts.Count > 0)
            {
                var fizzled = new List<int>();
                batch = spellResolver.BeginCasts(state, casts, launchEvents, fizzled);
                interrupted.UnionWith(fizzled);
            }

            // 2. Déplacements, téléportations et invocations.
            var allMoves = new List<PendingMove>(moves);
            allMoves.AddRange(batch.Placements);

            int firstBeat = result.Beats.Count;
            MovementResolver.Resolve(state, allMoves, result.Beats, random);

            var failedCasters = new HashSet<int>();
            foreach (PendingMove move in allMoves)
            {
                if (!move.Stopped)
                {
                    continue;
                }

                interrupted.Add(move.CasterId);
                if (move.Kind != PlacementKind.Walk)
                {
                    failedCasters.Add(move.CasterId);
                    launchEvents.Events.Add(new SpellFizzledEvent(
                        move.CasterId, move.SpellId, move.Path[0], "La case est déjà prise."));
                }
            }

            // Un sort dont la téléportation ou l'invocation a échoué n'a pas d'impact.
            batch.Active.RemoveAll(cast => failedCasters.Contains(cast.Caster.Id));

            // 3. Impacts, à la fin de l'action.
            var impactEvents = new ResolutionBeat();
            spellResolver.ApplyImpacts(state, batch.Active, impactEvents);

            AttachEvents(result, firstBeat, launchEvents, impactEvents);

            for (int i = firstBeat; i < result.Beats.Count; i++)
            {
                result.Beats[i].StepIndex = step;
            }
        }

        /// <summary>Les événements de lancement ouvrent l'action, ceux d'impact la ferment.</summary>
        private static void AttachEvents(RoundResult result, int firstBeat, ResolutionBeat launch, ResolutionBeat impact)
        {
            if (launch.Events.Count == 0 && impact.Events.Count == 0)
            {
                return;
            }

            if (result.Beats.Count == firstBeat)
            {
                var single = new ResolutionBeat();
                single.Events.AddRange(launch.Events);
                single.Events.AddRange(impact.Events);
                result.Beats.Add(single);
                return;
            }

            result.Beats[firstBeat].Events.InsertRange(0, launch.Events);
            result.Beats[result.Beats.Count - 1].Events.AddRange(impact.Events);
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
