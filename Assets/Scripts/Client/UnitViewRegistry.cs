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

        public static Color TeamColor(int teamId)
        {
            switch (teamId)
            {
                case MatchFactory.TeamBlue: return new Color(0.15f, 0.6f, 0.95f);
                case MatchFactory.TeamRed: return new Color(0.92f, 0.3f, 0.25f);
                default: return new Color(0.7f, 0.7f, 0.7f);
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
