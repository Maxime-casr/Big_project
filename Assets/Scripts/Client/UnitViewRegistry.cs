using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>Crée et retrouve les vues d'unités à partir de l'état du match.</summary>
    public sealed class UnitViewRegistry : MonoBehaviour
    {
        private readonly Dictionary<int, UnitView> views = new Dictionary<int, UnitView>();
        private MatchState state;
        private BoardView board;
        private GameObject unitPrefab;
        private int controlledUnitId = -1;

        public static readonly Color OwnColor = new Color(0.25f, 0.85f, 0.35f);
        public static readonly Color AllyColor = new Color(0.2f, 0.55f, 0.95f);
        public static readonly Color EnemyColor = new Color(0.92f, 0.28f, 0.25f);

        /// <summary>Couleurs sans point de vue (avant toute sélection) : une couleur par équipe.</summary>
        public static Color TeamColor(int teamId)
        {
            switch (teamId)
            {
                case MatchFactory.TeamBlue: return AllyColor;
                case MatchFactory.TeamRed: return EnemyColor;
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        /// <summary>Vert pour l'unité contrôlée, bleu pour ses alliés, rouge pour ses ennemis.</summary>
        public static Color ColorFor(UnitState unit, UnitState controlled)
        {
            if (controlled == null)
            {
                return TeamColor(unit.TeamId);
            }

            if (unit.Id == controlled.Id)
            {
                return OwnColor;
            }

            return unit.TeamId == controlled.TeamId ? AllyColor : EnemyColor;
        }

        /// <summary>Recolore toutes les unités du point de vue de l'unité contrôlée.</summary>
        public void ApplyPerspective(MatchState matchState, UnitState controlled)
        {
            controlledUnitId = controlled == null ? -1 : controlled.Id;
            foreach (UnitState unit in matchState.Units)
            {
                EnsureView(unit).SetColor(ColorFor(unit, controlled));
            }
        }

        /// <param name="prefab">
        /// Prefab du personnage (un SpriteRenderer, éventuellement un UnitView). Null : carrés générés.
        /// Aucun composant réseau n'est nécessaire : il n'affiche que l'état reçu.
        /// </param>
        public void Build(MatchState matchState, BoardView boardView, GameObject prefab = null)
        {
            state = matchState;
            board = boardView;
            unitPrefab = prefab;
            foreach (UnitState unit in state.Units)
            {
                EnsureView(unit);
            }
        }

        /// <summary>
        /// La vue d'une unité, créée si elle n'existe pas encore : les invocations apparaissent en cours de match,
        /// et leur vue doit exister avant que l'animation du tour ne les mentionne.
        /// </summary>
        public UnitView EnsureView(UnitState unit)
        {
            if (views.TryGetValue(unit.Id, out UnitView existing))
            {
                return existing;
            }

            GameObject prefab = GetPrefab(unit.CharacterId);
            GameObject go = prefab != null ? Instantiate(prefab, transform) : new GameObject();
            go.name = "Unit " + unit.Name + " (visuel uniquement)";
            go.transform.SetParent(transform, false);
            if (!go.TryGetComponent(out UnitView view))
            {
                view = go.AddComponent<UnitView>();
            }

            UnitState controlled = controlledUnitId < 0 ? null : state.GetUnit(controlledUnitId);
            view.Init(unit, board.CellToWorld(unit.Position), ColorFor(unit, controlled));
            views.Add(unit.Id, view);
            return view;
        }

        /// <summary>Le prefab d'un personnage : le sien, sinon le prefab par défaut du combat, sinon null (carré).</summary>
        public GameObject GetPrefab(string characterId)
        {
            GameObject own = GameContent.GetCharacterPrefab(characterId);
            return own != null ? own : unitPrefab;
        }

        /// <summary>Le SpriteRenderer du prefab d'un personnage, dont les fantômes transparents reprennent l'apparence.</summary>
        public SpriteRenderer GetSkinSource(string characterId)
        {
            GameObject prefab = GetPrefab(characterId);
            return prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>(true) : null;
        }

        public UnitView Get(int unitId)
        {
            return views[unitId];
        }

        public void SetSelected(int unitId)
        {
            foreach (KeyValuePair<int, UnitView> pair in views)
            {
                pair.Value.SetSelected(pair.Key == unitId);
            }
        }

        /// <summary>Aligne toutes les vues sur l'état réel (fin d'animation, resynchronisation).</summary>
        public void SyncAll(MatchState matchState)
        {
            foreach (UnitState unit in matchState.Units)
            {
                EnsureView(unit).Snap(board.CellToWorld(unit.Position), unit.Health, unit.IsAlive);
            }
        }
    }
}
