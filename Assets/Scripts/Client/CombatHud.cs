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
    /// Chaque élément peut être dessiné dans l'éditeur puis glissé dans le champ correspondant ; tout champ
    /// laissé vide est généré par code (au lancement, ou dans la scène via le menu
    /// "Big Project > Ajouter l'interface de combat au Canvas", pour pouvoir le déplacer et le restyler).
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color ButtonColor = new Color(0.2f, 0.25f, 0.32f, 0.95f);
        private static readonly Color ActiveColor = new Color(0.9f, 0.55f, 0.15f, 1f);
        private static readonly Color ConfirmColor = new Color(0.2f, 0.6f, 0.3f, 0.95f);

        [Header("Déplacement : un clic = une case de plus dans le plan")]
        [SerializeField] private Button moveUp;
        [SerializeField] private Button moveDown;
        [SerializeField] private Button moveLeft;
        [SerializeField] private Button moveRight;

        [Header("Cartes de sorts")]
        [SerializeField] private RectTransform cardContainer;

        [Header("Actions du tour")]
        [SerializeField] private Button undoButton;
        [SerializeField] private Button clearButton;
        [SerializeField] private Button confirmButton;
        [Tooltip("Test local uniquement : valide le tour de tous les joueurs d'un coup.")]
        [SerializeField] private Button confirmAllButton;
        [SerializeField] private Button recenterButton;

        [Header("Chrono et progression")]
        [SerializeField] private Text timerText;
        [SerializeField] private Text readyText;

        [Header("Minimap")]
        [SerializeField] private MinimapView minimap;

        private readonly List<CardView> cards = new List<CardView>();

        private PlanningController planning;
        private UnitViewRegistry units;
        private ResolutionPlayer resolution;
        private Font font;
        private GameObject switcherRow;
        private Text switcherLabel;
        private Button switcherPrevious;
        private Button switcherNext;
        private Text headerText;
        private Text selectedText;
        private Text ordersText;
        private Text messageText;
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
            BuildMissingElements();
            EnsureEventSystem();
            BuildInfoPanel();
        }

        /// <summary>
        /// Crée les éléments d'interface dont le champ est vide. Appelé au lancement, et par le menu de
        /// l'éditeur pour "poser" ces éléments dans la scène.
        /// </summary>
        public void BuildMissingElements()
        {
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (moveUp == null || moveDown == null || moveLeft == null || moveRight == null)
            {
                BuildDirectionPad();
            }

            if (cardContainer == null)
            {
                BuildCardContainer();
            }

            if (undoButton == null || clearButton == null || confirmButton == null ||
                confirmAllButton == null || recenterButton == null)
            {
                BuildActionsPanel();
            }

            if (timerText == null || readyText == null)
            {
                BuildTimer();
            }

            if (minimap == null)
            {
                BuildMinimap();
            }
        }

        public void Init(
            PlanningController planningController,
            UnitViewRegistry unitViews,
            ResolutionPlayer resolutionPlayer,
            CameraRig cameraRig,
            BoardView board)
        {
            planning = planningController;
            units = unitViews;
            resolution = resolutionPlayer;

            moveUp.onClick.AddListener(() => planning.AddStep(new GridPosition(0, 1)));
            moveDown.onClick.AddListener(() => planning.AddStep(new GridPosition(0, -1)));
            moveLeft.onClick.AddListener(() => planning.AddStep(new GridPosition(-1, 0)));
            moveRight.onClick.AddListener(() => planning.AddStep(new GridPosition(1, 0)));

            undoButton.onClick.AddListener(() => planning.UndoLastOrder());
            clearButton.onClick.AddListener(() => planning.ClearDraft());
            confirmButton.onClick.AddListener(() => planning.Submit());
            confirmAllButton.onClick.AddListener(() => planning.SubmitAll());
            recenterButton.onClick.AddListener(cameraRig.Recenter);

            minimap.Init(planning.State, board, cameraRig, planning);

            // Test local : un petit sélecteur "< nom >" pour passer d'un joueur à l'autre.
            switcherRow.SetActive(planning.IsMultiLocal);
            switcherPrevious.onClick.AddListener(planning.SelectPreviousLocalUnit);
            switcherNext.onClick.AddListener(planning.SelectNextLocalUnit);
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
            bool planningPhase = state.Phase == MatchPhase.Planning && !resolution.IsPlaying;

            headerText.text = "<b>Tour " + state.RoundNumber + "</b>\n" + PhaseText(state);
            RefreshTimer(state);
            RefreshSwitcher();
            RefreshSelection(selected, locked);

            moveUp.interactable = moveDown.interactable = moveLeft.interactable = moveRight.interactable =
                !locked && selected != null;
            undoButton.interactable = clearButton.interactable = !locked && selected != null;

            confirmButton.interactable = !locked && planningPhase;
            SetLabel(confirmButton, locked && planningPhase ? "Tour validé" : "Confirmer le tour");
            confirmAllButton.gameObject.SetActive(planning.IsMultiLocal);
            confirmAllButton.interactable = planningPhase;

            messageText.text = planning.Message;
        }

        private void RefreshTimer(MatchState state)
        {
            float seconds = planning.Session.PlanningSecondsRemaining;
            bool planningPhase = state.Phase == MatchPhase.Planning && !resolution.IsPlaying;

            if (resolution.IsPlaying)
            {
                timerText.text = "…";
                timerText.color = Color.white;
            }
            else if (seconds > 0f && planningPhase)
            {
                timerText.text = Mathf.CeilToInt(seconds).ToString();
                timerText.color = seconds <= 5f ? new Color(1f, 0.35f, 0.3f) : Color.white;
            }
            else
            {
                timerText.text = planningPhase ? "--" : string.Empty;
                timerText.color = Color.white;
            }

            readyText.text = planningPhase
                ? "Joueurs prêts : " + planning.Session.PlansReady + "/" + planning.Session.PlansExpected
                : string.Empty;
        }

        /// <summary>Nom du joueur contrôlé, ses PV (animés) et s'il a déjà confirmé son tour.</summary>
        private void RefreshSwitcher()
        {
            if (!switcherRow.activeSelf)
            {
                return;
            }

            UnitState unit = planning.SelectedUnit;
            if (unit == null)
            {
                switcherLabel.text = "—";
                return;
            }

            string label = unit.Name + "   PV " + units.Get(unit.Id).DisplayedHealth + "/" + unit.MaxHealth;
            if (planning.IsPlayerSubmitted(unit.PlayerId))
            {
                label += "   [prêt]";
            }

            switcherLabel.text = label;
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
                                "/" + selected.Mana + "\nAction " + (planning.CurrentActionIndex + 1) +
                                " (les alliés montrent la leur en transparence)";

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
                if (spell == null)
                {
                    continue;
                }

                string label = "<b>" + spell.DisplayName + "</b>\n" + spell.ManaCost + " mana\n" +
                               spell.DescribeTargeting() + "\n" + spell.DescribeEffects(GameContent.GetCharacterName);
                Button button = CreateButton(cardContainer, "Carte " + spell.Id, label, 120f, 11);

                Sprite icon = GameContent.GetIcon(spellId);
                if (icon != null)
                {
                    AddCardIcon(button, icon);
                }

                string captured = spellId;
                button.onClick.AddListener(() => planning.SetSpellTool(captured));
                cards.Add(new CardView { SpellId = spellId, Button = button, Image = button.GetComponent<Image>() });
            }
        }

        /// <summary>L'icône du sort en haut de la carte, le texte se décale dessous.</summary>
        private static void AddCardIcon(Button button, Sprite icon)
        {
            var go = new GameObject("Icône", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(button.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -4f);
            rect.sizeDelta = new Vector2(36f, 36f);

            var image = go.GetComponent<Image>();
            image.sprite = icon;
            image.preserveAspect = true;
            image.raycastTarget = false;

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                ((RectTransform)label.transform).offsetMax = new Vector2(-4f, -42f);
            }
        }

        private string Describe(UnitOrder order)
        {
            if (order is MoveOrder move)
            {
                return "Déplacement de " + move.Path.Count + " case(s) vers " + move.Path[move.Path.Count - 1];
            }

            var cast = (CastOrder)order;
            SpellDefinition spell = planning.Session.Spells.Get(cast.SpellId);
            switch (spell.Targeting)
            {
                case TargetKind.Self:
                    return spell.DisplayName + " (sur soi)";
                case TargetKind.Unit:
                    UnitState target = planning.State.GetUnit(cast.TargetUnitId);
                    return spell.DisplayName + " sur " + (target != null ? target.Name : "?");
                default:
                    return spell.DisplayName + " sur " + cast.Target;
            }
        }

        private string PhaseText(MatchState state)
        {
            if (state.Phase == MatchPhase.Finished)
            {
                return "Match terminé.";
            }

            return resolution.IsPlaying ? "Résolution en cours..." : "Planification : tout se joue en même temps.";
        }

        private static void SetLabel(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }

        // --- Construction des éléments ---

        private void BuildInfoPanel()
        {
            RectTransform panel = CreatePanel("Infos (généré)", transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(10f, -10f);
            panel.sizeDelta = new Vector2(380f, 0f);
            AddVerticalLayout(panel.gameObject, 4f, 8);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            headerText = CreateText(panel, "Titre", 16, TextAnchor.UpperLeft);

            BuildUnitSwitcher(panel);

            selectedText = CreateText(panel, "Sélection", 14, TextAnchor.UpperLeft);
            ordersText = CreateText(panel, "Ordres", 13, TextAnchor.UpperLeft);
            messageText = CreateText(panel, "Message", 13, TextAnchor.UpperLeft);

            Button quit = CreateButton(panel, "Quitter", "Quitter le match", 26f, 13);
            quit.onClick.AddListener(() => QuitRequested?.Invoke());
        }

        /// <summary>Une ligne compacte : [<]  nom du joueur contrôlé  [>]. Masquée hors test local.</summary>
        private void BuildUnitSwitcher(RectTransform parent)
        {
            var row = new GameObject("Changer de joueur (test)", typeof(RectTransform), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 26f;
            switcherRow = row;

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            switcherPrevious = CreateButton(row.transform, "Joueur précédent", "<", 26f, 16);
            switcherPrevious.GetComponent<LayoutElement>().preferredWidth = 32f;

            switcherLabel = CreateText(row.transform, "Joueur contrôlé", 13, TextAnchor.MiddleCenter);
            switcherLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            switcherNext = CreateButton(row.transform, "Joueur suivant", ">", 26f, 16);
            switcherNext.GetComponent<LayoutElement>().preferredWidth = 32f;
        }

        private void BuildDirectionPad()
        {
            RectTransform pad = CreatePanel("Déplacement (généré)", transform, PanelColor);
            pad.anchorMin = pad.anchorMax = pad.pivot = Vector2.zero;
            pad.anchoredPosition = new Vector2(10f, 10f);
            pad.sizeDelta = new Vector2(190f, 190f);

            moveUp = moveUp != null ? moveUp : CreatePadButton(pad, "Haut", "^", new Vector2(65f, 125f));
            moveDown = moveDown != null ? moveDown : CreatePadButton(pad, "Bas", "v", new Vector2(65f, 5f));
            moveLeft = moveLeft != null ? moveLeft : CreatePadButton(pad, "Gauche", "<", new Vector2(5f, 65f));
            moveRight = moveRight != null ? moveRight : CreatePadButton(pad, "Droite", ">", new Vector2(125f, 65f));
        }

        private void BuildCardContainer()
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

        private void BuildActionsPanel()
        {
            RectTransform panel = CreatePanel("Actions du tour (généré)", transform, PanelColor);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-10f, 0f);
            panel.sizeDelta = new Vector2(220f, 0f);
            AddVerticalLayout(panel.gameObject, 4f, 8);
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            undoButton = undoButton != null ? undoButton : CreateButton(panel, "Annuler dernière action", "Annuler la dernière action", 30f, 13);
            clearButton = clearButton != null ? clearButton : CreateButton(panel, "Tout annuler", "Tout annuler", 30f, 13);
            if (confirmButton == null)
            {
                confirmButton = CreateButton(panel, "Confirmer le tour", "Confirmer le tour", 44f, 16);
                confirmButton.GetComponent<Image>().color = ConfirmColor;
            }

            confirmAllButton = confirmAllButton != null ? confirmAllButton : CreateButton(panel, "Tout confirmer (test)", "Tout confirmer (test)", 30f, 13);
            recenterButton = recenterButton != null ? recenterButton : CreateButton(panel, "Recentrer la caméra", "Recentrer la caméra (Espace)", 28f, 12);
        }

        private void BuildTimer()
        {
            if (timerText == null)
            {
                RectTransform box = CreatePanel("Chrono (généré)", transform, PanelColor);
                box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
                box.anchoredPosition = new Vector2(0f, -10f);
                box.sizeDelta = new Vector2(120f, 56f);

                timerText = CreateText(box, "Texte", 34, TextAnchor.MiddleCenter);
                timerText.fontStyle = FontStyle.Bold;
                Stretch((RectTransform)timerText.transform);
            }

            if (readyText == null)
            {
                readyText = CreateText(transform, "Joueurs prêts (généré)", 15, TextAnchor.MiddleCenter);
                var rect = (RectTransform)readyText.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -70f);
                rect.sizeDelta = new Vector2(320f, 26f);
            }
        }

        private void BuildMinimap()
        {
            var go = new GameObject("Minimap (généré)", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(MinimapView));
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-10f, -10f);
            rect.sizeDelta = new Vector2(220f, 165f);
            go.GetComponent<RawImage>().color = new Color(0.1f, 0.1f, 0.12f, 1f);

            var frameObject = new GameObject("Cadre", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            frameObject.transform.SetParent(go.transform, false);
            var frameImage = frameObject.GetComponent<Image>();
            frameImage.color = new Color(1f, 1f, 1f, 0.12f);
            frameImage.raycastTarget = false;
            var outline = frameObject.GetComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            minimap = go.GetComponent<MinimapView>();
            minimap.Frame = (RectTransform)frameObject.transform;
        }

        private Button CreatePadButton(RectTransform parent, string objectName, string label, Vector2 position)
        {
            Button button = CreateButton(parent, objectName, label, 60f, 22);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(60f, 60f);
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

        private static void AddVerticalLayout(GameObject target, float spacing, int padding)
        {
            var layout = target.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
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

        private sealed class CardView
        {
            public string SpellId;
            public Button Button;
            public Image Image;
        }
    }
}
