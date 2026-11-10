using System.Collections.Generic;
using BigProject.Core.Match;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Copie transparente de l'unité contrôlée qui glisse vers sa position planifiée : le joueur voit
    /// où il sera à la fin de ses actions. La caméra suit ce fantôme pendant la planification.
    /// Les alliés ont leur propre fantôme, plus discret, à leur position à la fin de l'action de même numéro :
    /// de quoi éviter de se marcher dessus.
    /// </summary>
    public sealed class PlanGhost : MonoBehaviour
    {
        private const float Speed = 14f;

        private readonly Dictionary<int, SpriteRenderer> allyGhosts = new Dictionary<int, SpriteRenderer>();

        private PlanningController planning;
        private UnitViewRegistry units;
        private BoardView board;
        private ResolutionPlayer resolution;
        private SpriteRenderer body;
        private Transform allyRoot;

        public bool IsVisible => body != null && body.enabled;
        public Vector3 Position => transform.position;

        public void Init(PlanningController planningController, UnitViewRegistry unitViews, BoardView boardView, ResolutionPlayer resolutionPlayer)
        {
            planning = planningController;
            units = unitViews;
            board = boardView;
            resolution = resolutionPlayer;

            body = SpriteFactory.CreateRenderer("Fantôme", transform, new Color(UnitViewRegistry.OwnColor.r, UnitViewRegistry.OwnColor.g, UnitViewRegistry.OwnColor.b, 0.5f), 19);
            body.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            body.enabled = false;

            // Séparé de ce fantôme : celui-ci se déplace, les fantômes alliés se placent en coordonnées du monde.
            allyRoot = new GameObject("Fantômes alliés").transform;
            allyRoot.SetParent(transform.parent, false);
        }

        private void Update()
        {
            if (planning == null)
            {
                return;
            }

            UpdateAllyGhosts();

            var unit = planning.SelectedUnit;
            if (unit == null || !unit.IsAlive || resolution.IsPlaying)
            {
                body.enabled = false;
                return;
            }

            Vector3 unitPosition = units.Get(unit.Id).transform.position;
            Vector3 target = board.CellToWorld(planning.GetProjection(unit).FinalPosition);
            bool atUnit = (target - unitPosition).sqrMagnitude < 0.0001f;

            if (!body.enabled)
            {
                if (atUnit)
                {
                    return;
                }

                // Apparition : le fantôme part de l'unité.
                transform.position = unitPosition;
                body.enabled = true;
            }

            transform.position = Vector3.MoveTowards(transform.position, target, Speed * Time.deltaTime);

            // Revenu sur l'unité (actions annulées) : le fantôme disparaît.
            if (atUnit && (transform.position - unitPosition).sqrMagnitude < 0.0001f)
            {
                body.enabled = false;
            }
        }

        private void UpdateAllyGhosts()
        {
            var visible = new HashSet<int>();

            if (!resolution.IsPlaying && planning.State.Phase == MatchPhase.Planning)
            {
                foreach (PlanningController.AllyPreview preview in planning.GetAllyPreviews())
                {
                    int id = preview.Unit.Id;
                    Vector3 unitPosition = units.Get(id).transform.position;
                    Vector3 target = board.CellToWorld(preview.Position);

                    if (!allyGhosts.TryGetValue(id, out SpriteRenderer ghost))
                    {
                        var color = UnitViewRegistry.AllyColor;
                        ghost = SpriteFactory.CreateRenderer("Fantôme allié " + preview.Unit.Name, allyRoot, new Color(color.r, color.g, color.b, 0.35f), 18);
                        ghost.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
                        ghost.enabled = false;
                        allyGhosts.Add(id, ghost);
                    }

                    bool atUnit = (target - unitPosition).sqrMagnitude < 0.0001f;
                    if (!ghost.enabled)
                    {
                        if (atUnit)
                        {
                            continue;
                        }

                        ghost.transform.position = unitPosition;
                        ghost.enabled = true;
                    }

                    ghost.transform.position = Vector3.MoveTowards(ghost.transform.position, target, Speed * Time.deltaTime);
                    if (atUnit && (ghost.transform.position - unitPosition).sqrMagnitude < 0.0001f)
                    {
                        ghost.enabled = false;
                        continue;
                    }

                    visible.Add(id);
                }
            }

            foreach (KeyValuePair<int, SpriteRenderer> pair in allyGhosts)
            {
                if (!visible.Contains(pair.Key))
                {
                    pair.Value.enabled = false;
                }
            }
        }
    }
}
