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
        private BoardView board;

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
        public void ApplyPerspective(MatchState state, UnitState controlled)
        {
            foreach (UnitState unit in state.Units)
            {
                views[unit.Id].SetColor(ColorFor(unit, controlled));
            }
        }

        public void Build(MatchState state, BoardView boardView)
        {
            board = boardView;
            foreach (UnitState unit in state.Units)
            {
                var go = new GameObject("Unit " + unit.Name + " (visuel uniquement)");
                go.transform.SetParent(transform, false);
                UnitView view = go.AddComponent<UnitView>();
                view.Init(unit, board.CellToWorld(unit.Position), TeamColor(unit.TeamId));
                views.Add(unit.Id, view);
            }
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
        public void SyncAll(MatchState state)
        {
            foreach (UnitState unit in state.Units)
            {
                views[unit.Id].Snap(board.CellToWorld(unit.Position), unit.Health, unit.IsAlive);
            }
        }
    }
}
