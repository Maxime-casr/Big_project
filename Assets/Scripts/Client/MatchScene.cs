using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Units;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace BigProject.Client
{
    /// <summary>
    /// Assemble tous les composants d'un combat pour une session donnée, hors ligne ou en ligne.
    /// Détruire ce GameObject supprime toute la scène de combat.
    /// </summary>
    public sealed class MatchScene : MonoBehaviour
    {
        private IMatchSession session;
        private ResolutionPlayer resolution;

        public event Action QuitRequested;

        public static MatchScene Create(
            Transform parent,
            Camera camera,
            IMatchSession session,
            IEnumerable<int> localPlayerIds)
        {
            var root = new GameObject("Match");
            root.transform.SetParent(parent, false);
            MatchScene scene = root.AddComponent<MatchScene>();
            scene.Build(camera, session, localPlayerIds);
            return scene;
        }

        private void Build(Camera camera, IMatchSession matchSession, IEnumerable<int> localPlayerIds)
        {
            session = matchSession;

            BoardView board = CreateChild<BoardView>("Board");
            board.Build(session.State.Map, FindSceneTilemap(session.State.Map));

            TileHighlighter highlighter = CreateChild<TileHighlighter>("Highlights");
            highlighter.Init(board);

            UnitViewRegistry units = CreateChild<UnitViewRegistry>("Units");
            units.Build(session.State, board);

            GridInput input = CreateChild<GridInput>("Input");
            input.Init(camera, board);

            resolution = CreateChild<ResolutionPlayer>("Resolution");
            resolution.Init(units, board, highlighter);
            session.RoundResolved += OnRoundResolved;

            PlanningController planning = CreateChild<PlanningController>("Planning");
            planning.Init(session, highlighter, units, input, resolution, localPlayerIds);

            PlanGhost ghost = CreateChild<PlanGhost>("Fantôme");
            ghost.Init(planning, units, board, resolution);

            // La caméra suit le fantôme pendant la planification, puis l'unité pendant la résolution.
            CameraRig rig = camera.GetComponent<CameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<CameraRig>();
            }

            // Le HUD est celui du Canvas de la scène ; à défaut, un Canvas généré est créé.
            CombatHud hud = FindAnyObjectByType<CombatHud>();
            if (hud == null)
            {
                hud = CombatHud.CreateFallback();
                hud.transform.SetParent(transform, false);
            }

            rig.Init(
                board,
                () =>
                {
                    UnitState unit = planning.SelectedUnit;
                    if (unit == null)
                    {
                        return null;
                    }

                    return ghost.IsVisible ? ghost.Position : units.Get(unit.Id).transform.position;
                },
                () => hud.IsPointerOverUi);

            hud.Init(planning, units, resolution, rig, board);
            hud.QuitRequested += () => QuitRequested?.Invoke();
            input.IsPointerBlocked = _ => hud.IsPointerOverUi;

            planning.SelectionChanged += () => units.ApplyPerspective(session.State, planning.SelectedUnit);
            planning.FocusRequested += rig.Recenter;
            units.ApplyPerspective(session.State, planning.SelectedUnit);
        }

        /// <summary>
        /// Tilemap de la scène à utiliser pour l'affichage, si elle correspond à la carte du match.
        /// Sinon (autre carte côté serveur, par exemple), elle est masquée et le terrain est généré :
        /// mieux vaut un affichage simple qu'une carte qui ne correspond pas aux règles.
        /// </summary>
        private static Tilemap FindSceneTilemap(GridMap map)
        {
            MapSource source = FindAnyObjectByType<MapSource>();
            if (source == null || source.Ground == null)
            {
                return null;
            }

            if (source.Matches(map))
            {
                return source.Ground;
            }

            Debug.LogWarning(
                "La Tilemap de la scène ne correspond pas à la carte du match (" + map.Width + "x" + map.Height +
                "). Terrain généré à la place. Enregistre la scène pour régénérer la carte du serveur.");
            source.Ground.gameObject.SetActive(false);
            return null;
        }

        private void OnRoundResolved(RoundResult result)
        {
            resolution.Play(result, session.State);
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.RoundResolved -= OnRoundResolved;
            }
        }

        private T CreateChild<T>(string objectName) where T : Component
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
