using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Units;
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
        private readonly List<SpriteRenderer> ownSummonGhosts = new List<SpriteRenderer>();
        private readonly List<SpriteRenderer> allySummonGhosts = new List<SpriteRenderer>();

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
            ApplySkin(body, 0.7f);
            body.enabled = false;

            // Séparé de ce fantôme : celui-ci se déplace, les fantômes alliés se placent en coordonnées du monde.
            allyRoot = new GameObject("Fantômes alliés").transform;
            allyRoot.SetParent(transform.parent, false);
        }

        /// <summary>
        /// Les fantômes reprennent l'apparence des personnages : sprite, matériau et échelle du prefab s'il y en a
        /// un, sinon un carré. La teinte (verte pour soi, bleue pour les alliés) et la transparence s'y ajoutent.
        /// </summary>
        private void ApplySkin(SpriteRenderer ghost, float squareScale)
        {
            UnitView sample = units.Get(planning.State.Units[0].Id);
            if (sample != null && sample.HasCustomBody)
            {
                SpriteRenderer source = sample.BodyRenderer;
                ghost.sprite = source.sprite;
                ghost.sharedMaterial = source.sharedMaterial;
                ghost.flipX = source.flipX;
                ghost.flipY = source.flipY;
                ghost.transform.localScale = source.transform.lossyScale;
                return;
            }

            ghost.transform.localScale = new Vector3(squareScale, squareScale, 1f);
        }

        private void Update()
        {
            if (planning == null)
            {
                return;
            }

            var previews = !resolution.IsPlaying && planning.State.Phase == MatchPhase.Planning
                ? planning.GetAllyPreviews()
                : new List<PlanningController.AllyPreview>();

            UpdateAllyGhosts(previews);
            UpdateSummonGhosts(previews);

            var unit = planning.SelectedUnit;
            if (unit == null || !unit.IsAlive || resolution.IsPlaying)
            {
                body.enabled = false;
                return;
            }

            Vector3 unitPosition = units.EnsureView(unit).transform.position;
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

        private void UpdateAllyGhosts(List<PlanningController.AllyPreview> previews)
        {
            var visible = new HashSet<int>();

            foreach (PlanningController.AllyPreview preview in previews)
            {
                int id = preview.Unit.Id;
                Vector3 unitPosition = units.EnsureView(preview.Unit).transform.position;
                Vector3 target = board.CellToWorld(preview.Position);

                if (!allyGhosts.TryGetValue(id, out SpriteRenderer ghost))
                {
                    var color = UnitViewRegistry.AllyColor;
                    ghost = SpriteFactory.CreateRenderer("Fantôme allié " + preview.Unit.Name, allyRoot, new Color(color.r, color.g, color.b, 0.35f), 18);
                    ApplySkin(ghost, 0.6f);
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

            foreach (KeyValuePair<int, SpriteRenderer> pair in allyGhosts)
            {
                if (!visible.Contains(pair.Key))
                {
                    pair.Value.enabled = false;
                }
            }
        }

        /// <summary>
        /// Les unités qui vont être invoquées, en transparent : les siennes et celles des alliés (à l'action
        /// courante), pour ne pas se déplacer là où elles apparaîtront. Celles des ennemis ne sont jamais montrées.
        /// </summary>
        private void UpdateSummonGhosts(List<PlanningController.AllyPreview> previews)
        {
            var own = new List<GridPosition>();
            var allies = new List<GridPosition>();

            UnitState me = planning.SelectedUnit;
            if (me != null && me.IsAlive && previews != null && !resolution.IsPlaying &&
                planning.State.Phase == MatchPhase.Planning)
            {
                own.AddRange(planning.GetProjection(me).SummonCells);
                foreach (PlanningController.AllyPreview preview in previews)
                {
                    allies.AddRange(preview.SummonCells);
                }
            }

            FillSummonGhosts(ownSummonGhosts, own, UnitViewRegistry.OwnColor);
            FillSummonGhosts(allySummonGhosts, allies, UnitViewRegistry.AllyColor);
        }

        private void FillSummonGhosts(List<SpriteRenderer> pool, List<GridPosition> cells, Color color)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i == pool.Count)
                {
                    SpriteRenderer ghost = SpriteFactory.CreateRenderer(
                        "Fantôme d'invocation", allyRoot, new Color(color.r, color.g, color.b, 0.3f), 18);
                    ApplySkin(ghost, 0.6f);
                    pool.Add(ghost);
                }

                pool[i].enabled = true;
                pool[i].transform.position = board.CellToWorld(cells[i]);
            }

            for (int i = cells.Count; i < pool.Count; i++)
            {
                pool[i].enabled = false;
            }
        }
    }
}
