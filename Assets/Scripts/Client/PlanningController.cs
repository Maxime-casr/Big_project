using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BigProject.Client
{
    public enum PlanningTool
    {
        Select,
        Move,
        Spell
    }

    /// <summary>
    /// Gère la phase de planification du joueur local : sélection d'une unité, construction de sa
    /// liste d'actions ordonnée (déplacements, sorts), prévisualisations, puis envoi à la session.
    /// Toute règle est déléguée au Core (PlanValidator, Pathfinder, SpellTargeting).
    /// </summary>
    public sealed class PlanningController : MonoBehaviour
    {
        private readonly Dictionary<int, List<UnitOrder>> drafts = new Dictionary<int, List<UnitOrder>>();
        private readonly HashSet<int> localPlayerIds = new HashSet<int>();

        private IMatchSession session;
        private BoardView board;
        private TileHighlighter highlighter;
        private UnitViewRegistry units;
        private ResolutionPlayer resolutionPlayer;
        private GridPosition? hoveredCell;
        private bool submitted;
        private float previousSecondsRemaining;

        public PlanningTool Tool { get; private set; }
        public int SelectedUnitId { get; private set; } = -1;
        public string ActiveSpellId { get; private set; }
        public string Message { get; private set; } = "Sélectionne une unité pour planifier ses actions.";

        public MatchState State => session.State;
        public IMatchSession Session => session;
        public bool IsLocked =>
            submitted || resolutionPlayer.IsPlaying || session.State.Phase != MatchPhase.Planning;

        public void Init(
            IMatchSession matchSession,
            BoardView boardView,
            TileHighlighter tileHighlighter,
            UnitViewRegistry unitViews,
            GridInput input,
            ResolutionPlayer player,
            IEnumerable<int> controlledPlayerIds)
        {
            session = matchSession;
            board = boardView;
            highlighter = tileHighlighter;
            units = unitViews;
            resolutionPlayer = player;

            foreach (int playerId in controlledPlayerIds)
            {
                localPlayerIds.Add(playerId);
            }

            input.CellClicked += OnCellClicked;
            input.CellRightClicked += _ => UndoLastOrder();
            input.HoverChanged += OnHoverChanged;
            session.RoundResolved += OnRoundResolved;
            session.PlanRejected += OnPlanRejected;
            resolutionPlayer.PlaybackFinished += RefreshHighlights;
            previousSecondsRemaining = session.PlanningSecondsRemaining;
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.RoundResolved -= OnRoundResolved;
                session.PlanRejected -= OnPlanRejected;
            }
        }

        public UnitState SelectedUnit => SelectedUnitId < 0 ? null : session.State.GetUnit(SelectedUnitId);

        public bool IsControlledLocally(UnitState unit)
        {
            return localPlayerIds.Contains(unit.PlayerId);
        }

        public IReadOnlyList<UnitOrder> GetDraft(int unitId)
        {
            return drafts.TryGetValue(unitId, out List<UnitOrder> list)
                ? (IReadOnlyList<UnitOrder>)list
                : Array.Empty<UnitOrder>();
        }

        public PlanProjection GetProjection(UnitState unit)
        {
            return PlanValidator.Project(session.State, session.Spells, unit, GetDraft(unit.Id));
        }

        public void SelectUnit(int unitId)
        {
            UnitState unit = session.State.GetUnit(unitId);
            if (unit == null || !unit.IsAlive || !IsControlledLocally(unit))
            {
                return;
            }

            SelectedUnitId = unitId;
            Tool = PlanningTool.Move;
            units.SetSelected(unitId);
            Message = unit.Name + " sélectionné.";
            RefreshHighlights();
        }

        public void SetTool(PlanningTool tool)
        {
            Tool = tool;
            RefreshHighlights();
        }

        public void SetSpellTool(string spellId)
        {
            ActiveSpellId = spellId;
            Tool = PlanningTool.Spell;
            RefreshHighlights();
        }

        public void UndoLastOrder()
        {
            if (IsLocked || !drafts.TryGetValue(SelectedUnitId, out List<UnitOrder> list) || list.Count == 0)
            {
                return;
            }

            list.RemoveAt(list.Count - 1);
            Message = "Dernière action annulée.";
            RefreshHighlights();
        }

        /// <summary>
        /// Avance d'une case dans la direction donnée (boutons de direction). Prolonge le déplacement en
        /// cours de planification ; la direction opposée annule le dernier pas.
        /// </summary>
        public void AddStep(GridPosition direction)
        {
            UnitState unit = SelectedUnit;
            if (IsLocked || unit == null)
            {
                Message = unit == null ? "Sélectionne d'abord une unité." : Message;
                return;
            }

            var candidate = new List<UnitOrder>(GetDraft(unit.Id));
            int lastIndex = candidate.Count - 1;

            if (lastIndex >= 0 && candidate[lastIndex] is MoveOrder lastMove)
            {
                var withoutLast = new List<UnitOrder>(candidate);
                withoutLast.RemoveAt(lastIndex);
                GridPosition moveStart = PlanValidator.Project(session.State, session.Spells, unit, withoutLast).FinalPosition;

                var path = new List<GridPosition>(lastMove.Path);
                GridPosition end = path[path.Count - 1];
                GridPosition previous = path.Count >= 2 ? path[path.Count - 2] : moveStart;

                if (end + direction == previous)
                {
                    path.RemoveAt(path.Count - 1);
                    if (path.Count == 0)
                    {
                        candidate.RemoveAt(lastIndex);
                    }
                    else
                    {
                        candidate[lastIndex] = new MoveOrder(path);
                    }

                    drafts[unit.Id] = candidate;
                    Message = "Pas annulé.";
                    RefreshHighlights();
                    return;
                }

                path.Add(end + direction);
                candidate[lastIndex] = new MoveOrder(path);
            }
            else
            {
                GridPosition start = PlanValidator.Project(session.State, session.Spells, unit, candidate).FinalPosition;
                candidate.Add(new MoveOrder(new[] { start + direction }));
            }

            PlanProjection projection = PlanValidator.Project(session.State, session.Spells, unit, candidate);
            if (!projection.IsValid)
            {
                Message = projection.Error;
                return;
            }

            drafts[unit.Id] = candidate;
            Message = "Pas ajouté.";
            RefreshHighlights();
        }

        public void ClearDraft()
        {
            if (IsLocked)
            {
                return;
            }

            drafts.Remove(SelectedUnitId);
            Message = "Actions effacées.";
            RefreshHighlights();
        }

        /// <summary>Envoie le plan de chaque joueur contrôlé localement.</summary>
        public void Submit()
        {
            if (IsLocked)
            {
                return;
            }

            var plans = new List<TurnPlan>();
            foreach (int playerId in session.State.GetActivePlayerIds())
            {
                if (!localPlayerIds.Contains(playerId))
                {
                    continue;
                }

                var unitPlans = new List<UnitPlan>();
                foreach (UnitState unit in session.State.Units)
                {
                    if (unit.IsAlive && unit.PlayerId == playerId)
                    {
                        unitPlans.Add(new UnitPlan(unit.Id, GetDraft(unit.Id)));
                    }
                }

                plans.Add(new TurnPlan(playerId, unitPlans));
            }

            submitted = true;
            highlighter.ClearAll();
            foreach (TurnPlan plan in plans)
            {
                if (!session.TrySubmitPlan(plan, out string error))
                {
                    Message = error;
                    submitted = false;
                    RefreshHighlights();
                    break;
                }
            }
        }

        private void Update()
        {
            // Temps écoulé : on envoie ce qui est planifié, le serveur ne peut pas attendre davantage.
            float remaining = session.PlanningSecondsRemaining;
            bool timedOut = previousSecondsRemaining > 0f && remaining <= 0f;
            previousSecondsRemaining = remaining;
            if (timedOut && !IsLocked)
            {
                Submit();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || IsLocked)
            {
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetTool(PlanningTool.Select);
            }
            else if (keyboard.backspaceKey.wasPressedThisFrame)
            {
                UndoLastOrder();
            }
            else if (keyboard.enterKey.wasPressedThisFrame)
            {
                Submit();
            }
        }

        private void OnCellClicked(GridPosition cell)
        {
            if (IsLocked)
            {
                return;
            }

            switch (Tool)
            {
                case PlanningTool.Select:
                    UnitState clicked = session.State.GetUnitAt(cell);
                    if (clicked != null && IsControlledLocally(clicked))
                    {
                        SelectUnit(clicked.Id);
                    }

                    break;
                case PlanningTool.Move:
                    UnitState friendly = session.State.GetUnitAt(cell);
                    if (friendly != null && IsControlledLocally(friendly) && friendly.Id != SelectedUnitId)
                    {
                        SelectUnit(friendly.Id);
                    }
                    else
                    {
                        TryAddMove(cell);
                    }

                    break;
                case PlanningTool.Spell:
                    TryAddCast(cell);
                    break;
            }
        }

        private void OnHoverChanged(GridPosition? cell)
        {
            hoveredCell = cell;
            RefreshHighlights();
        }

        private void TryAddMove(GridPosition target)
        {
            UnitState unit = SelectedUnit;
            if (unit == null)
            {
                Message = "Sélectionne d'abord une unité.";
                return;
            }

            PlanProjection projection = GetProjection(unit);
            int remaining = unit.MovementPoints - projection.MovementUsed;
            List<GridPosition> path = Pathfinder.FindPath(session.State.Map, projection.FinalPosition, target, remaining);
            if (path == null)
            {
                Message = "Case inaccessible avec les PM restants (" + remaining + ").";
                return;
            }

            TryAppend(unit, new MoveOrder(path), "Déplacement de " + path.Count + " case(s) ajouté.");
        }

        private void TryAddCast(GridPosition target)
        {
            UnitState unit = SelectedUnit;
            if (unit == null || ActiveSpellId == null)
            {
                Message = "Sélectionne une unité et un sort.";
                return;
            }

            SpellDefinition spell = session.Spells.Get(ActiveSpellId);
            TryAppend(unit, new CastOrder(ActiveSpellId, target), spell.DisplayName + " ajouté.");
        }

        private void TryAppend(UnitState unit, UnitOrder order, string successMessage)
        {
            var candidate = new List<UnitOrder>(GetDraft(unit.Id)) { order };
            PlanProjection projection = PlanValidator.Project(session.State, session.Spells, unit, candidate);
            if (!projection.IsValid)
            {
                Message = projection.Error;
                return;
            }

            drafts[unit.Id] = candidate;
            Message = successMessage;
            RefreshHighlights();
        }

        private void OnPlanRejected(string reason)
        {
            submitted = false;
            Message = "Plan refusé par le serveur : " + reason;
            RefreshHighlights();
        }

        private void OnRoundResolved(RoundResult result)
        {
            drafts.Clear();
            submitted = false;
            Message = "Tour " + result.RoundNumber + " résolu.";
            if (result.RejectedPlans.Count > 0)
            {
                Message += " Plans refusés : " + result.RejectedPlans.Count + ".";
            }

            if (result.MatchEnded)
            {
                Message = result.WinnerTeamId == MatchState.NoTeam
                    ? "Match terminé : égalité."
                    : "Match terminé : victoire de l'équipe " + (result.WinnerTeamId == MatchFactory.TeamBlue ? "bleue" : "rouge") + ".";
            }

            highlighter.ClearAll();
        }

        private void RefreshHighlights()
        {
            highlighter.ClearAll();
            if (IsLocked)
            {
                return;
            }

            var plannedMoves = new List<GridPosition>();
            var plannedCasts = new List<GridPosition>();
            foreach (KeyValuePair<int, List<UnitOrder>> draft in drafts)
            {
                foreach (UnitOrder order in draft.Value)
                {
                    if (order is MoveOrder move)
                    {
                        plannedMoves.AddRange(move.Path);
                    }
                    else if (order is CastOrder cast)
                    {
                        plannedCasts.Add(cast.Target);
                    }
                }
            }

            highlighter.Set(HighlightLayer.PlannedMove, plannedMoves);
            highlighter.Set(HighlightLayer.PlannedCast, plannedCasts);

            UnitState unit = SelectedUnit;
            if (unit == null)
            {
                return;
            }

            PlanProjection projection = GetProjection(unit);
            GridPosition origin = projection.FinalPosition;
            GridMap map = session.State.Map;

            if (Tool == PlanningTool.Move)
            {
                int remaining = unit.MovementPoints - projection.MovementUsed;
                List<GridPosition> reachable = Pathfinder.GetReachable(map, origin, remaining);
                highlighter.Set(HighlightLayer.Reachable, reachable);

                if (hoveredCell.HasValue && reachable.Contains(hoveredCell.Value))
                {
                    highlighter.Set(HighlightLayer.Path, Pathfinder.FindPath(map, origin, hoveredCell.Value, remaining));
                }
            }
            else if (Tool == PlanningTool.Spell && ActiveSpellId != null)
            {
                SpellDefinition spell = session.Spells.Get(ActiveSpellId);
                List<GridPosition> castable = SpellTargeting.GetCastableCells(map, spell, origin);
                highlighter.Set(HighlightLayer.CastRange, castable);

                if (hoveredCell.HasValue && castable.Contains(hoveredCell.Value))
                {
                    highlighter.Set(
                        HighlightLayer.Area,
                        SpellArea.GetCells(map, spell.AreaShape, spell.AreaRadius, hoveredCell.Value));
                }
            }
        }
    }
}
