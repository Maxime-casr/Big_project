using System;
using System.Collections.Generic;
using System.Text;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BigProject.Client
{
    /// <summary>
    /// Interface de combat en uGUI, à poser sur le Canvas de la scène.
    /// Les éléments déjà dessinés dans l'éditeur sont réutilisés (boutons de direction, conteneur de cartes) :
    /// il suffit de les glisser dans les champs ci-dessous. Tout champ laissé vide est remplacé par une
    /// version générée par code, ce qui permet de remplacer les éléments un à un par ses propres designs.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.25f, 0.32f, 0.95f);
        private static readonly Color ActiveColor = new Color(0.9f, 0.55f, 0.15f, 1f);
        private static readonly Color SelectedColor = new Color(0.25f, 0.5f, 0.8f, 1f);

        [Header("Déplacement : un clic = une case de plus dans le plan (vide = généré)")]
        [SerializeField] private Button moveUp;
        [SerializeField] private Button moveDown;
        [SerializeField] private Button moveLeft;
        [SerializeField] private Button moveRight;

        [Header("Conteneur des cartes de sorts (vide = généré)")]
        [SerializeField] private RectTransform cardContainer;

        private readonly List<UnitRow> unitRows = new List<UnitRow>();
        private readonly List<CardView> cards = new List<CardView>();

        private PlanningController planning;
        private UnitViewRegistry units;
        private ResolutionPlayer resolution;
        private Font font;
        private RectTransform unitsContainer;
        private Text headerText;
        private Text selectedText;
        private Text ordersText;
        private Text messageText;
        private Button undoButton;
        private Button clearButton;
        private Button submitButton;
        private int cardsBuiltForUnit = -2;

        public event Action QuitRequested;

        public bool IsPointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        /// <summary>Crée un Canvas complet avec un HUD généré, pour les scènes de combat qui n'en ont pas.</summary>
        public static CombatHud CreateFallback()
        {
            var go = new GameObject("Canvas (HUD généré)", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go.AddComponent<CombatHud>();
        }

        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            BuildInfoPanel();

            if (moveUp == null || moveDown == null || moveLeft == null || moveRight == null)
            {
                BuildDefaultDirectionPad();
            }

            if (cardContainer == null)
            {
                BuildDefaultCardContainer();
            }
        }

        public void Init(PlanningController planningController, UnitViewRegistry unitViews, ResolutionPlayer resolutionPlayer)
        {
            planning = planningController;
            units = unitViews;
            resolution = resolutionPlayer;

            moveUp.onClick.AddListener(() => planning.AddStep(new GridPosition(0, 1)));
            moveDown.onClick.AddListener(() => planning.AddStep(new GridPosition(0, -1)));
            moveLeft.onClick.AddListener(() => planning.AddStep(new GridPosition(-1, 0)));
            moveRight.onClick.AddListener(() => planning.AddStep(new GridPosition(1, 0)));

            foreach (UnitState unit in planning.State.Units)
            {
                UnitState captured = unit;
                Button button = CreateButton(unitsContainer, "Unité " + unit.Name, string.Empty, 26f, 13);
                button.onClick.AddListener(() => planning.SelectUnit(captured.Id));
                unitRows.Add(new UnitRow
                {
                    Unit = unit,
                    Button = button,
                    Image = button.GetComponent<Image>(),
                    Label = button.GetComponentInChildren<Text>()
                });
            }
        }

        private void Update()
        {
            if (planning == null)
            {
                return;
            }

            MatchState state = planning.State;
            UnitState selected = planning.SelectedUnit;
            bool locked = planning.IsLocked;

            headerText.text = "<b>Tour " + state.RoundNumber + "</b>" + TimerText(state) + "\n" + PhaseText(state);
            RefreshUnitRows(locked);
            RefreshSelection(selected, locked);

            moveUp.interactable = moveDown.interactable = moveLeft.interactable = moveRight.interactable =
                !locked && selected != null;
            undoButton.interactable = clearButton.interactable = !locked && selected != null;
            submitButton.interactable = !locked && state.Phase == MatchPhase.Planning;
            messageText.text = planning.Message;
        }

        private void RefreshUnitRows(bool locked)
        {
            foreach (UnitRow row in unitRows)
            {
                UnitState unit = row.Unit;
                int health = units.Get(unit.Id).DisplayedHealth;
                bool defeated = !unit.IsAlive && health <= 0;

                row.Label.text = defeated
                    ? unit.Name + "  (vaincu)"
                    : unit.Name + "   PV " + health + "/" + unit.MaxHealth + "   Mana " + unit.Mana + "/" + unit.MaxMana;
                row.Button.interactable = !locked && unit.IsAlive && planning.IsControlledLocally(unit);
                row.Image.color = planning.SelectedUnitId == unit.Id ? SelectedColor : ButtonColor;
            }
        }

        private void RefreshSelection(UnitState selected, bool locked)
        {
            int selectedId = selected == null ? -1 : selected.Id;
            if (cardsBuiltForUnit != selectedId)
            {
                RebuildCards(selected);
                cardsBuiltForUnit = selectedId;
            }

            if (selected == null)
            {
                selectedText.text = "Aucune unité sélectionnée.";
                ordersText.text = string.Empty;
                return;
            }

            PlanProjection projection = planning.GetProjection(selected);
            selectedText.text = "<b>" + selected.Name + "</b>   PM : " + (selected.MovementPoints - projection.MovementUsed) +
                                "/" + selected.MovementPoints + "   Mana : " + (selected.Mana - projection.ManaUsed) +
                                "/" + selected.Mana;

            var builder = new StringBuilder("<b>Actions planifiées</b>");
            IReadOnlyList<UnitOrder> draft = planning.GetDraft(selected.Id);
            if (draft.Count == 0)
            {
                builder.Append("\nAucune");
            }

            for (int i = 0; i < draft.Count; i++)
            {
                builder.Append('\n').Append(i + 1).Append(". ").Append(Describe(draft[i]));
            }

            ordersText.text = builder.ToString();

            foreach (CardView card in cards)
            {
                SpellDefinition spell = planning.Session.Spells.Get(card.SpellId);
                bool affordable = spell.ManaCost <= selected.Mana - projection.ManaUsed;
                card.Button.interactable = !locked && affordable;
                card.Image.color = planning.Tool == PlanningTool.Spell && planning.ActiveSpellId == card.SpellId
                    ? ActiveColor
                    : ButtonColor;
            }
        }

        private void RebuildCards(UnitState selected)
        {
            foreach (CardView card in cards)
            {
                Destroy(card.Button.gameObject);
            }

            cards.Clear();
            if (selected == null)
            {
                return;
            }

            foreach (string spellId in selected.SpellIds)
            {
                SpellDefinition spell = planning.Session.Spells.Get(spellId);
                string label = "<b>" + spell.DisplayName + "</b>\n" + spell.ManaCost + " mana\nportée " +
                               spell.MinRange + "-" + spell.MaxRange;
                Button button = CreateButton(cardContainer, "Carte " + spell.Id, label, 120f, 12);
                string captured = spellId;
                button.onClick.AddListener(() => planning.SetSpellTool(captured));
                cards.Add(new CardView { SpellId = spellId, Button = button, Image = button.GetComponent<Image>() });
            }
        }

        private string Describe(UnitOrder order)
        {
            if (order is MoveOrder move)
            {
                return "Déplacement de " + move.Path.Count + " case(s) vers " + move.Path[move.Path.Count - 1];
            }

            var cast = (CastOrder)order;
            return planning.Session.Spells.Get(cast.SpellId).DisplayName + " sur " + cast.Target;
        }

        private string TimerText(MatchState state)
        {
            float seconds = planning.Session.PlanningSecondsRemaining;
            if (seconds <= 0f || state.Phase != MatchPhase.Planning || resolution.IsPlaying)
            {
                return string.Empty;
            }

            return "   —   " + Mathf.CeilToInt(seconds) + " s";
        }

        private string PhaseText(MatchState state)
        {
            if (state.Phase == MatchPhase.Finished)
            {
                return "Match terminé.";
            }

            return resolution.IsPlaying ? "Résolution en cours..." : "Planification : tout se joue en même temps.";
        }

        // --- Construction de l'interface par défaut ---

        private void BuildInfoPanel()
        {
            RectTransform panel = CreatePanel("Infos (généré)", transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(10f, -10f);
            panel.sizeDelta = new Vector2(380f, 0f);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            headerText = CreateText(panel, "Titre", 16, TextAnchor.UpperLeft);

            unitsContainer = new GameObject("Unités", typeof(RectTransform)).GetComponent<RectTransform>();
            unitsContainer.SetParent(panel, false);
            var unitsLayout = unitsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            unitsLayout.spacing = 2f;
            unitsLayout.childControlWidth = true;
            unitsLayout.childControlHeight = true;
            unitsLayout.childForceExpandWidth = true;
            unitsLayout.childForceExpandHeight = false;

            selectedText = CreateText(panel, "Sélection", 14, TextAnchor.UpperLeft);
            ordersText = CreateText(panel, "Ordres", 13, TextAnchor.UpperLeft);

            var buttonsRow = new GameObject("Boutons", typeof(RectTransform)).GetComponent<RectTransform>();
            buttonsRow.SetParent(panel, false);
            var rowLayout = buttonsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 4f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            undoButton = CreateButton(buttonsRow, "Annuler", "Annuler la dernière", 28f, 13);
            undoButton.onClick.AddListener(() => planning.UndoLastOrder());
            clearButton = CreateButton(buttonsRow, "Effacer", "Tout effacer", 28f, 13);
            clearButton.onClick.AddListener(() => planning.ClearDraft());

            submitButton = CreateButton(panel, "Valider", "Valider le tour", 38f, 16);
            submitButton.onClick.AddListener(() => planning.Submit());

            messageText = CreateText(panel, "Message", 13, TextAnchor.UpperLeft);

            Button quit = CreateButton(panel, "Quitter", "Quitter le match", 26f, 13);
            quit.onClick.AddListener(() => QuitRequested?.Invoke());
        }

        private void BuildDefaultDirectionPad()
        {
            RectTransform pad = CreatePanel("Déplacement (généré)", transform, PanelColor);
            pad.anchorMin = pad.anchorMax = pad.pivot = Vector2.zero;
            pad.anchoredPosition = new Vector2(10f, 10f);
            pad.sizeDelta = new Vector2(190f, 190f);

            moveUp = CreatePadButton(pad, "Haut", "^", new Vector2(65f, 125f));
            moveDown = CreatePadButton(pad, "Bas", "v", new Vector2(65f, 5f));
            moveLeft = CreatePadButton(pad, "Gauche", "<", new Vector2(5f, 65f));
            moveRight = CreatePadButton(pad, "Droite", ">", new Vector2(125f, 65f));
        }

        private void BuildDefaultCardContainer()
        {
            RectTransform panel = CreatePanel("Cartes (généré)", transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 10f);
            panel.sizeDelta = new Vector2(4f * 90f + 3f * 15f + 20f, 140f);

            var grid = panel.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(10, 10, 10, 10);
            grid.cellSize = new Vector2(90f, 120f);
            grid.spacing = new Vector2(15f, 0f);
            cardContainer = panel;
        }

        private Button CreatePadButton(RectTransform parent, string objectName, string label, Vector2 position)
        {
            Button button = CreateButton(parent, objectName, label, 60f, 22);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(60f, 60f);
            Destroy(button.GetComponent<LayoutElement>());
            return button;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.hideFlags = HideFlags.DontSave;
        }

        private static RectTransform CreatePanel(string objectName, Transform parent, Color color)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        private Text CreateText(Transform parent, string objectName, int size, TextAnchor alignment)
        {
            var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string objectName, string label, float preferredHeight, int fontSize)
        {
            var go = new GameObject(
                objectName,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = ButtonColor;
            go.GetComponent<Button>().targetGraphic = image;
            go.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            Text text = CreateText(go.transform, "Texte", fontSize, TextAnchor.MiddleCenter);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-4f, -2f);
            text.text = label;

            return go.GetComponent<Button>();
        }

        private sealed class UnitRow
        {
            public UnitState Unit;
            public Button Button;
            public Image Image;
            public Text Label;
        }

        private sealed class CardView
        {
            public string SpellId;
            public Button Button;
            public Image Image;
        }
    }
}
