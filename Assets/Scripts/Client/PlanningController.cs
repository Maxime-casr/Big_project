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
    /// Gère la phase de planification du joueur local : choix de l'unité contrôlée, construction de sa
    /// liste d'actions ordonnée (déplacements, sorts), prévisualisations, puis envoi à la session.
    /// Toute règle est déléguée au Core (PlanValidator, Pathfinder, SpellTargeting).
    ///
    /// En test local, plusieurs joueurs sont contrôlés depuis un seul poste : l'unité sélectionnée est
    /// celle que l'on "joue", et chaque joueur valide son tour séparément (ou tous d'un coup).
    /// </summary>
    public sealed class PlanningController : MonoBehaviour
    {
        private readonly Dictionary<int, List<UnitOrder>> drafts = new Dictionary<int, List<UnitOrder>>();
        private readonly HashSet<int> localPlayerIds = new HashSet<int>();
        private readonly HashSet<int> submittedPlayers = new HashSet<int>();

        private IMatchSession session;
        private TileHighlighter highlighter;
        private UnitViewRegistry units;
        private ResolutionPlayer resolutionPlayer;
        private GridPosition? hoveredCell;
        private float previousSecondsRemaining;
        private bool draftDirty;
        private float lastDraftSent;
        private float nextAllyRefresh;

        /// <summary>Ce qu'un allié prévoit à l'action courante (et où il sera à la fin de celle-ci).</summary>
        public struct AllyPreview
        {
            public UnitState Unit;
            public GridPosition Position;
            public UnitOrder CurrentOrder;

            /// <summary>Les cases où il aura invoqué une unité d'ici cette action : à montrer en transparent.</summary>
            public IReadOnlyList<GridPosition> SummonCells;

            /// <summary>Le personnage invoqué à chacune de ces cases (même ordre).</summary>
            public IReadOnlyList<string> SummonCharacterIds;
        }

        public PlanningTool Tool { get; private set; }
        public int SelectedUnitId { get; private set; } = -1;
        public string ActiveSpellId { get; private set; }
        public string Message { get; private set; } = "Sélectionne une unité pour planifier ses actions.";

        /// <summary>L'unité contrôlée a changé : la vue doit se recolorer.</summary>
        public event Action SelectionChanged;

        /// <summary>La caméra doit revenir sur l'unité contrôlée.</summary>
        public event Action FocusRequested;

        public MatchState State => session.State;
        public IMatchSession Session => session;
        public UnitState SelectedUnit => SelectedUnitId < 0 ? null : session.State.GetUnit(SelectedUnitId);

        /// <summary>Plusieurs joueurs sont contrôlés depuis ce poste (test local).</summary>
        public bool IsMultiLocal => localPlayerIds.Count > 1;

        public bool IsLocked =>
            resolutionPlayer.IsPlaying ||
            session.State.Phase != MatchPhase.Planning ||
            (SelectedUnit != null && submittedPlayers.Contains(SelectedUnit.PlayerId));

        public void Init(
            IMatchSession matchSession,
            TileHighlighter tileHighlighter,
            UnitViewRegistry unitViews,
            GridInput input,
            ResolutionPlayer player,
            IEnumerable<int> controlledPlayerIds)
        {
            session = matchSession;
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

            SelectFirstLocalUnit();
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.RoundResolved -= OnRoundResolved;
                session.PlanRejected -= OnPlanRejected;
            }
        }

        public bool IsControlledLocally(UnitState unit)
        {
            return localPlayerIds.Contains(unit.PlayerId);
        }

        public bool IsPlayerSubmitted(int playerId)
        {
            return submittedPlayers.Contains(playerId);
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

        /// <summary>Numéro (0 = première) de l'action en cours de planification pour l'unité contrôlée.</summary>
        public int CurrentActionIndex
        {
            get
            {
                UnitState unit = SelectedUnit;
                return unit == null ? 0 : Math.Max(0, GetDraft(unit.Id).Count - 1);
            }
        }

        /// <summary>
        /// Les alliés de l'unité contrôlée, à la même action qu'elle : où ils seront à la fin de leur action
        /// de même numéro, et ce qu'ils y font. Les ennemis n'apparaissent jamais ici.
        /// </summary>
        public List<AllyPreview> GetAllyPreviews()
        {
            var previews = new List<AllyPreview>();
            UnitState me = SelectedUnit;
            if (me == null)
            {
                return previews;
            }

            int index = CurrentActionIndex;
            foreach (UnitState ally in session.State.Units)
            {
                if (!ally.IsAlive || ally.Id == me.Id || ally.TeamId != me.TeamId)
                {
                    continue;
                }

                IReadOnlyList<UnitOrder> orders = IsControlledLocally(ally)
                    ? GetDraft(ally.Id)
                    : (session.TryGetAllyDraft(ally.Id, out IReadOnlyList<UnitOrder> received)
                        ? received
                        : Array.Empty<UnitOrder>());

                int count = Math.Min(orders.Count, index + 1);
                var prefix = new List<UnitOrder>(count);
                for (int i = 0; i < count; i++)
                {
                    prefix.Add(orders[i]);
                }

                PlanProjection projection = PlanValidator.Project(session.State, session.Spells, ally, prefix);
                previews.Add(new AllyPreview
                {
                    Unit = ally,
                    Position = projection.IsValid ? projection.FinalPosition : ally.Position,
                    CurrentOrder = orders.Count > index ? orders[index] : null,
                    SummonCells = projection.IsValid ? projection.SummonCells : new List<GridPosition>(),
                    SummonCharacterIds = projection.IsValid ? projection.SummonCharacterIds : new List<string>()
                });
            }

            return previews;
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
            SelectionChanged?.Invoke();
            FocusRequested?.Invoke();
        }

        /// <summary>Passe au joueur local suivant encore en vie (test local).</summary>
        public void SelectNextLocalUnit()
        {
            SelectAdjacentLocalUnit(1);
        }

        /// <summary>Revient au joueur local précédent encore en vie (test local).</summary>
        public void SelectPreviousLocalUnit()
        {
            SelectAdjacentLocalUnit(-1);
        }

        private void SelectAdjacentLocalUnit(int direction)
        {
            IReadOnlyList<UnitState> all = session.State.Units;
            int start = -1;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == SelectedUnitId)
                {
                    start = i;
                }
            }

            for (int offset = 1; offset <= all.Count; offset++)
            {
                int index = ((start + direction * offset) % all.Count + all.Count) % all.Count;
                UnitState candidate = all[index];
                if (candidate.IsAlive && IsControlledLocally(candidate))
                {
                    SelectUnit(candidate.Id);
                    return;
                }
            }
        }

        public void SetTool(PlanningTool tool)
        {
            Tool = tool;
            RefreshHighlights();
        }

        /// <summary>
        /// Choisit un sort. Un sort sur soi n'a pas de cible : il est ajouté tout de suite au plan. Sinon l'outil
        /// de sort est activé et le joueur clique la case ou l'unité visée.
        /// </summary>
        public void SetSpellTool(string spellId)
        {
            SpellDefinition spell = session.Spells.Get(spellId);
            UnitState unit = SelectedUnit;
            if (spell != null && spell.Targeting == TargetKind.Self && unit != null && !IsLocked)
            {
                GridPosition position = GetProjection(unit).FinalPosition;
                TryAppend(unit, new CastOrder(spellId, position, unit.Id), spell.DisplayName + " ajouté.");
                return;
            }

            ActiveSpellId = spellId;
            Tool = PlanningTool.Spell;
            string description = GameContent.GetDescription(spellId);
            if (spell != null)
            {
                Message = spell.DisplayName + (string.IsNullOrEmpty(description) ? string.Empty : " — " + description);
            }

            RefreshHighlights();
        }

        public void UndoLastOrder()
        {
            if (IsLocked || !drafts.TryGetValue(SelectedUnitId, out List<UnitOrder> list) || list.Count == 0)
            {
                return;
            }

            list.RemoveAt(list.Count - 1);
            draftDirty = true;
            Message = "Dernière action annulée.";
            RefreshHighlights();
        }

        public void ClearDraft()
        {
            if (IsLocked)
            {
                return;
            }

            drafts.Remove(SelectedUnitId);
            draftDirty = true;
            Message = "Actions effacées.";
            RefreshHighlights();
        }

        /// <summary>
        /// Avance d'une case dans la direction donnée (boutons de direction). Prolonge le déplacement en
        /// cours de planification ; la direction opposée annule le dernier pas.
        /// </summary>
        public void AddStep(GridPosition direction)
        {
            UnitState unit = SelectedUnit;
            if (unit == null)
            {
                Message = "Sélectionne d'abord une unité.";
                return;
            }

            if (IsLocked)
            {
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
                    draftDirty = true;
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
            draftDirty = true;
            Message = "Pas ajouté.";
            RefreshHighlights();
            FocusRequested?.Invoke();
        }

        /// <summary>Valide le tour du joueur contrôlé.</summary>
        public void Submit()
        {
            UnitState unit = SelectedUnit;
            if (unit != null)
            {
                SubmitPlayers(new[] { unit.PlayerId });
            }
        }

        /// <summary>Valide le tour de tous les joueurs contrôlés qui ne l'ont pas encore fait.</summary>
        public void SubmitAll()
        {
            SubmitPlayers(new List<int>(localPlayerIds));
        }

        private void SubmitPlayers(IEnumerable<int> playerIds)
        {
            if (resolutionPlayer.IsPlaying || session.State.Phase != MatchPhase.Planning)
            {
                return;
            }

            var plans = new List<TurnPlan>();
            foreach (int playerId in new SortedSet<int>(playerIds))
            {
                if (submittedPlayers.Contains(playerId) || !session.State.GetActivePlayerIds().Contains(playerId))
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

            foreach (TurnPlan plan in plans)
            {
                // Marqué avant l'envoi : en local, la résolution peut se déclencher pendant TrySubmitPlan.
                submittedPlayers.Add(plan.PlayerId);
                if (!session.TrySubmitPlan(plan, out string error))
                {
                    submittedPlayers.Remove(plan.PlayerId);
                    Message = error;
                    break;
                }
            }

            if (resolutionPlayer.IsPlaying)
            {
                return;
            }

            if (plans.Count > 0 && submittedPlayers.Count > 0)
            {
                Message = "Tour validé : en attente des autres joueurs.";
            }

            RefreshHighlights();
        }

        private void Update()
        {
            // Temps écoulé : on envoie ce qui est planifié, le serveur ne peut pas attendre davantage.
            float remaining = session.PlanningSecondsRemaining;
            bool timedOut = previousSecondsRemaining > 0f && remaining <= 0f;
            previousSecondsRemaining = remaining;
            if (timedOut)
            {
                SubmitAll();
            }

            SyncDraftAndAllies();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                FocusRequested?.Invoke();
            }

            if (keyboard.tabKey.wasPressedThisFrame && IsMultiLocal)
            {
                SelectNextLocalUnit();
            }

            if (IsLocked)
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

        /// <summary>Partage le brouillon avec les alliés (au plus 10 fois par seconde) et rafraîchit leur aperçu.</summary>
        private void SyncDraftAndAllies()
        {
            if (resolutionPlayer.IsPlaying || session.State.Phase != MatchPhase.Planning)
            {
                return;
            }

            if (draftDirty && Time.unscaledTime - lastDraftSent >= 0.1f)
            {
                draftDirty = false;
                lastDraftSent = Time.unscaledTime;

                var unitPlans = new List<UnitPlan>();
                foreach (UnitState unit in session.State.Units)
                {
                    if (unit.IsAlive && IsControlledLocally(unit) && !submittedPlayers.Contains(unit.PlayerId))
                    {
                        unitPlans.Add(new UnitPlan(unit.Id, GetDraft(unit.Id)));
                    }
                }

                if (unitPlans.Count > 0)
                {
                    session.SendDraft(unitPlans);
                }
            }

            if (Time.unscaledTime >= nextAllyRefresh)
            {
                nextAllyRefresh = Time.unscaledTime + 0.2f;
                UpdateAllyLayers();
            }
        }

        /// <summary>
        /// Montre ce que font les alliés à l'action courante : leur trajet, ou la zone de leur sort dans une
        /// couleur différente de la nôtre. Leurs autres actions restent masquées pour garder la lisibilité.
        /// </summary>
        private void UpdateAllyLayers()
        {
            var moveCells = new List<GridPosition>();
            var castCells = new List<GridPosition>();

            foreach (AllyPreview preview in GetAllyPreviews())
            {
                if (preview.CurrentOrder is MoveOrder move)
                {
                    moveCells.AddRange(move.Path);
                }
                else if (preview.CurrentOrder is CastOrder cast)
                {
                    SpellDefinition spell = session.Spells.Get(cast.SpellId);
                    if (spell == null || spell.Targeting == TargetKind.Self)
                    {
                        continue;
                    }

                    // Sort sur une unité : seule la case de sa cible. Sort sur une case : toute la zone.
                    if (spell.Targeting == TargetKind.Unit)
                    {
                        castCells.Add(cast.Target);
                    }
                    else
                    {
                        castCells.AddRange(SpellArea.GetCells(session.State.Map, spell.AreaShape, spell.AreaRadius, cast.Target));
                    }
                }
            }

            highlighter.Set(HighlightLayer.AllyMove, moveCells);
            highlighter.Set(HighlightLayer.AllyCast, castCells);
        }

        private void SelectFirstLocalUnit()
        {
            foreach (UnitState unit in session.State.Units)
            {
                if (unit.IsAlive && IsControlledLocally(unit))
                {
                    SelectedUnitId = unit.Id;
                    Tool = PlanningTool.Move;
                    units.SetSelected(unit.Id);
                    Message = unit.Name + " : planifie tes actions puis confirme.";
                    return;
                }
            }
        }

        private void OnCellClicked(GridPosition cell)
        {
            UnitState clicked = session.State.GetUnitAt(cell);
            bool clickedLocalUnit = clicked != null && IsControlledLocally(clicked);

            // Cliquer une autre de ses unités la sélectionne (sauf pour la cibler avec un sort).
            if (clickedLocalUnit && Tool != PlanningTool.Spell && (clicked.Id != SelectedUnitId || Tool == PlanningTool.Select))
            {
                SelectUnit(clicked.Id);
                return;
            }

            if (IsLocked || Tool == PlanningTool.Select)
            {
                return;
            }

            if (Tool == PlanningTool.Move)
            {
                TryAddMove(cell);
            }
            else
            {
                TryAddCast(cell);
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

            if (TryAppend(unit, new MoveOrder(path), "Déplacement de " + path.Count + " case(s) ajouté."))
            {
                FocusRequested?.Invoke();
            }
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
            if (spell.Targeting == TargetKind.Unit)
            {
                UnitState targetUnit = session.State.GetUnitAt(target);
                if (targetUnit == null)
                {
                    Message = "Clique sur une unité : " + spell.DisplayName + " vise une unité.";
                    return;
                }

                TryAppend(
                    unit,
                    new CastOrder(ActiveSpellId, targetUnit.Position, targetUnit.Id),
                    spell.DisplayName + " sur " + targetUnit.Name + " ajouté.");
                return;
            }

            TryAppend(unit, new CastOrder(ActiveSpellId, target), spell.DisplayName + " ajouté.");
        }

        private bool TryAppend(UnitState unit, UnitOrder order, string successMessage)
        {
            var candidate = new List<UnitOrder>(GetDraft(unit.Id)) { order };
            PlanProjection projection = PlanValidator.Project(session.State, session.Spells, unit, candidate);
            if (!projection.IsValid)
            {
                Message = projection.Error;
                return false;
            }

            drafts[unit.Id] = candidate;
            draftDirty = true;
            Message = successMessage;
            RefreshHighlights();
            return true;
        }

        private void OnPlanRejected(string reason)
        {
            submittedPlayers.Clear();
            Message = "Plan refusé par le serveur : " + reason;
            RefreshHighlights();
        }

        private void OnRoundResolved(RoundResult result)
        {
            drafts.Clear();
            submittedPlayers.Clear();
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

            UnitState selected = SelectedUnit;
            if (selected == null || !selected.IsAlive)
            {
                SelectNextLocalUnit();
            }

            highlighter.ClearAll();
            FocusRequested?.Invoke();
        }

        private void RefreshHighlights()
        {
            highlighter.ClearAll();
            if (resolutionPlayer.IsPlaying)
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
            UpdateAllyLayers();

            UnitState unit = SelectedUnit;
            if (unit == null || IsLocked)
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
                if (spell.Targeting == TargetKind.Unit)
                {
                    // On montre les unités que l'on peut viser depuis la position prévue ; au survol, la zone
                    // qui serait touchée autour de la cible.
                    List<UnitState> targetable = SpellTargeting.GetTargetableUnits(map, spell, origin, unit, session.State.Units);
                    var targetCells = new List<GridPosition>();
                    foreach (UnitState candidate in targetable)
                    {
                        targetCells.Add(candidate.Position);
                    }

                    highlighter.Set(HighlightLayer.CastRange, targetCells);

                    if (hoveredCell.HasValue && targetCells.Contains(hoveredCell.Value))
                    {
                        highlighter.Set(
                            HighlightLayer.Area,
                            SpellArea.GetCells(map, spell.AreaShape, spell.AreaRadius, hoveredCell.Value));
                    }

                    return;
                }

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
