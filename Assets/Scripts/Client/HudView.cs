using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Interface provisoire en IMGUI : suffisante pour tester les règles.
    /// À remplacer par UI Toolkit ou uGUI quand le design sera défini ; elle ne fait que lire
    /// le PlanningController et lui envoyer des commandes.
    /// </summary>
    public sealed class HudView : MonoBehaviour
    {
        private const float MaxPanelWidth = 380f;

        private PlanningController planning;
        private UnitViewRegistry units;
        private ResolutionPlayer resolution;

        public float PanelWidth => Mathf.Min(MaxPanelWidth, Screen.width * 0.4f);

        public event System.Action QuitRequested;

        public void Init(PlanningController planningController, UnitViewRegistry unitViews, ResolutionPlayer resolutionPlayer)
        {
            planning = planningController;
            units = unitViews;
            resolution = resolutionPlayer;
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            return screenPoint.x >= Screen.width - PanelWidth;
        }

        private void OnGUI()
        {
            if (planning == null)
            {
                return;
            }

            MatchState state = planning.State;
            GUILayout.BeginArea(new Rect(Screen.width - PanelWidth, 0f, PanelWidth, Screen.height), GUI.skin.box);

            GUILayout.Label("<b>Tour " + state.RoundNumber + "</b>" + TimerText(), RichLabel());
            GUILayout.Label(PhaseText(state));
            GUILayout.Space(6f);

            DrawUnitList(state);
            GUILayout.Space(6f);
            DrawSelectedUnit(state);

            GUILayout.FlexibleSpace();
            GUILayout.Label(planning.Message);
            DrawSubmitButton(state);
            GUILayout.Label("Clic droit / Retour : annuler · Échap : sélection · Entrée : valider");
            if (GUILayout.Button("Quitter le match"))
            {
                QuitRequested?.Invoke();
            }

            GUILayout.EndArea();
        }

        private string TimerText()
        {
            float seconds = planning.Session.PlanningSecondsRemaining;
            if (seconds <= 0f || planning.State.Phase != MatchPhase.Planning || resolution.IsPlaying)
            {
                return string.Empty;
            }

            return "   —   temps restant : " + Mathf.CeilToInt(seconds) + " s";
        }

        private void DrawUnitList(MatchState state)
        {
            GUILayout.Label("<b>Unités</b>", RichLabel());
            foreach (UnitState unit in state.Units)
            {
                int health = units.Get(unit.Id).DisplayedHealth;
                string label = unit.Name + "   PV " + health + "/" + unit.MaxHealth + "   Mana " + unit.Mana + "/" + unit.MaxMana;
                if (!unit.IsAlive && health <= 0)
                {
                    label = unit.Name + "   (vaincu)";
                }

                bool wasEnabled = GUI.enabled;
                GUI.enabled = wasEnabled && unit.IsAlive && !planning.IsLocked && planning.IsControlledLocally(unit);
                if (GUILayout.Toggle(planning.SelectedUnitId == unit.Id, label, GUI.skin.button) &&
                    planning.SelectedUnitId != unit.Id)
                {
                    planning.SelectUnit(unit.Id);
                }

                GUI.enabled = wasEnabled;
            }
        }

        private void DrawSelectedUnit(MatchState state)
        {
            UnitState unit = planning.SelectedUnit;
            if (unit == null)
            {
                return;
            }

            PlanProjection projection = planning.GetProjection(unit);
            GUILayout.Label("<b>" + unit.Name + "</b>   PM restants : " + (unit.MovementPoints - projection.MovementUsed) +
                            "/" + unit.MovementPoints + "   Mana restant : " + (unit.Mana - projection.ManaUsed) +
                            "/" + unit.Mana, RichLabel());

            GUI.enabled = !planning.IsLocked;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(planning.Tool == PlanningTool.Select, "Sélection", GUI.skin.button))
            {
                if (planning.Tool != PlanningTool.Select)
                {
                    planning.SetTool(PlanningTool.Select);
                }
            }

            if (GUILayout.Toggle(planning.Tool == PlanningTool.Move, "Déplacement", GUI.skin.button))
            {
                if (planning.Tool != PlanningTool.Move)
                {
                    planning.SetTool(PlanningTool.Move);
                }
            }

            GUILayout.EndHorizontal();

            foreach (string spellId in unit.SpellIds)
            {
                SpellDefinition spell = planning.Session.Spells.Get(spellId);
                bool active = planning.Tool == PlanningTool.Spell && planning.ActiveSpellId == spellId;
                string label = spell.DisplayName + "  (" + spell.ManaCost + " mana, portée " + spell.MinRange + "-" + spell.MaxRange + ")";
                if (GUILayout.Toggle(active, label, GUI.skin.button) && !active)
                {
                    planning.SetSpellTool(spellId);
                }
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>Actions planifiées (dans l'ordre)</b>", RichLabel());
            IReadOnlyList<UnitOrder> draft = planning.GetDraft(unit.Id);
            if (draft.Count == 0)
            {
                GUILayout.Label("Aucune");
            }

            for (int i = 0; i < draft.Count; i++)
            {
                GUILayout.Label((i + 1) + ". " + Describe(draft[i]));
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Annuler la dernière"))
            {
                planning.UndoLastOrder();
            }

            if (GUILayout.Button("Tout effacer"))
            {
                planning.ClearDraft();
            }

            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawSubmitButton(MatchState state)
        {
            GUI.enabled = !planning.IsLocked && state.Phase == MatchPhase.Planning;
            if (GUILayout.Button("Valider le tour", GUILayout.Height(36f)))
            {
                planning.Submit();
            }

            GUI.enabled = true;
        }

        private string Describe(UnitOrder order)
        {
            if (order is MoveOrder move)
            {
                return "Déplacement (" + move.Path.Count + " case(s)) vers " + move.Path[move.Path.Count - 1];
            }

            var cast = (CastOrder)order;
            return planning.Session.Spells.Get(cast.SpellId).DisplayName + " sur " + cast.Target;
        }

        private string PhaseText(MatchState state)
        {
            if (state.Phase == MatchPhase.Finished)
            {
                return "Match terminé.";
            }

            return resolution.IsPlaying ? "Résolution en cours..." : "Planification : toutes les actions seront jouées en même temps.";
        }

        private static GUIStyle RichLabel()
        {
            var style = new GUIStyle(GUI.skin.label) { richText = true };
            return style;
        }
    }
}
