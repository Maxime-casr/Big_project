using System;
using System.Collections.Generic;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using UnityEngine;

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
            board.Build(session.State.Map);

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
            planning.Init(session, board, highlighter, units, input, resolution, localPlayerIds);

            // Le HUD est celui du Canvas de la scène ; à défaut, un Canvas généré est créé.
            CombatHud hud = FindAnyObjectByType<CombatHud>();
            if (hud == null)
            {
                hud = CombatHud.CreateFallback();
                hud.transform.SetParent(transform, false);
            }

            hud.Init(planning, units, resolution);
            hud.QuitRequested += () => QuitRequested?.Invoke();
            input.IsPointerBlocked = _ => hud.IsPointerOverUi;

            CameraRig rig = camera.GetComponent<CameraRig>();
            if (rig == null)
            {
                rig = camera.gameObject.AddComponent<CameraRig>();
            }

            rig.Init(session.State.Map, () => 0f);
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
