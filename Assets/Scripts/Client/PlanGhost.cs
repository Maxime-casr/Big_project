using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Copie transparente de l'unité contrôlée qui glisse vers sa position planifiée : le joueur voit
    /// où il sera à la fin de ses actions. La caméra suit ce fantôme pendant la planification.
    /// Les alliés ont leur propre fantôme, plus discret, à leur position à la fin de l'action de même numéro,
    /// et les invocations prévues apparaissent en transparent : de quoi éviter de se marcher dessus.
    /// Chaque fantôme reprend l'apparence de son personnage.
    /// </summary>
    public sealed class PlanGhost : MonoBehaviour
    {
        private const float Speed = 14f;
        private const int OwnOrder = 19;
        private const int OtherOrder = 18;

        private readonly Dictionary<int, SpriteRenderer> allyGhosts = new Dictionary<int, SpriteRenderer>();
        private readonly List<GhostSlot> ownSummonGhosts = new List<GhostSlot>();
        private readonly List<GhostSlot> allySummonGhosts = new List<GhostSlot>();

        private PlanningController planning;
        private UnitViewRegistry units;
        private BoardView board;
        private ResolutionPlayer resolution;
        private SpriteRenderer body;
        private string bodyCharacterId;
        private Transform allyRoot;

        public bool IsVisible => body != null && body.enabled;
        public Vector3 Position => transform.position;

        private sealed class GhostSlot
        {
            public SpriteRenderer Renderer;
            public string CharacterId;
        }

        public void Init(PlanningController planningController, UnitViewRegistry unitViews, BoardView boardView, ResolutionPlayer resolutionPlayer)
        {
            planning = planningController;
            units = unitViews;
            board = boardView;
            resolution = resolutionPlayer;

            // Séparé de ce fantôme : celui-ci se déplace, les autres se placent en coordonnées du monde.
            allyRoot = new GameObject("Fantômes alliés").transform;
            allyRoot.SetParent(transform.parent, false);
        }

        /// <summary>
        /// Crée un fantôme à l'apparence d'un personnage : sprite, matériau et échelle de son prefab s'il en a un,
        /// sinon un carré. La teinte (verte pour soi, bleue pour les alliés) et la transparence s'y ajoutent.
        /// </summary>
        private SpriteRenderer CreateGhost(string objectName, Transform parent, Color color, int order, string characterId, float squareScale)
        {
            SpriteRenderer ghost = SpriteFactory.CreateRenderer(objectName, parent, color, order);
            SpriteRenderer source = units.GetSkinSource(characterId);
            if (source != null)
            {
                ghost.sprite = source.sprite;
                ghost.sharedMaterial = source.sharedMaterial;
                ghost.flipX = source.flipX;
                ghost.flipY = source.flipY;
                ghost.transform.localScale = source.transform.lossyScale;
            }
            else
            {
                ghost.transform.localScale = new Vector3(squareScale, squareScale, 1f);
            }

            return ghost;
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
            UpdateOwnGhost();
        }

        private void UpdateOwnGhost()
        {
            UnitState unit = planning.SelectedUnit;
            if (unit == null || !unit.IsAlive || resolution.IsPlaying)
            {
                if (body != null)
                {
                    body.enabled = false;
                }

                return;
            }

            // Le fantôme change d'apparence quand on passe à un autre personnage (test local).
            if (body == null || bodyCharacterId != unit.CharacterId)
            {
                bool wasVisible = body != null && body.enabled;
                if (body != null)
                {
                    Destroy(body.gameObject);
                }

                var own = UnitViewRegistry.OwnColor;
                body = CreateGhost("Fantôme", transform, new Color(own.r, own.g, own.b, 0.5f), OwnOrder, unit.CharacterId, 0.7f);
                body.enabled = wasVisible;
                bodyCharacterId = unit.CharacterId;
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
                    ghost = CreateGhost(
                        "Fantôme allié " + preview.Unit.Name, allyRoot, new Color(color.r, color.g, color.b, 0.35f),
                        OtherOrder, preview.Unit.CharacterId, 0.6f);
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
            var ownCells = new List<GridPosition>();
            var ownCharacters = new List<string>();
            var allyCells = new List<GridPosition>();
            var allyCharacters = new List<string>();

            UnitState me = planning.SelectedUnit;
            if (me != null && me.IsAlive && !resolution.IsPlaying && planning.State.Phase == MatchPhase.Planning)
            {
                PlanProjection own = planning.GetProjection(me);
                ownCells.AddRange(own.SummonCells);
                ownCharacters.AddRange(own.SummonCharacterIds);

                foreach (PlanningController.AllyPreview preview in previews)
                {
                    allyCells.AddRange(preview.SummonCells);
                    allyCharacters.AddRange(preview.SummonCharacterIds);
                }
            }

            FillSummonGhosts(ownSummonGhosts, ownCells, ownCharacters, UnitViewRegistry.OwnColor);
            FillSummonGhosts(allySummonGhosts, allyCells, allyCharacters, UnitViewRegistry.AllyColor);
        }

        private void FillSummonGhosts(List<GhostSlot> pool, List<GridPosition> cells, List<string> characterIds, Color color)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                string characterId = i < characterIds.Count ? characterIds[i] : string.Empty;
                if (i == pool.Count)
                {
                    pool.Add(new GhostSlot());
                }

                GhostSlot slot = pool[i];
                if (slot.Renderer == null || slot.CharacterId != characterId)
                {
                    if (slot.Renderer != null)
                    {
                        Destroy(slot.Renderer.gameObject);
                    }

                    slot.Renderer = CreateGhost(
                        "Fantôme d'invocation", allyRoot, new Color(color.r, color.g, color.b, 0.3f), OtherOrder, characterId, 0.6f);
                    slot.CharacterId = characterId;
                }

                slot.Renderer.enabled = true;
                slot.Renderer.transform.position = board.CellToWorld(cells[i]);
            }

            for (int i = cells.Count; i < pool.Count; i++)
            {
                if (pool[i].Renderer != null)
                {
                    pool[i].Renderer.enabled = false;
                }
            }
        }
    }
}
