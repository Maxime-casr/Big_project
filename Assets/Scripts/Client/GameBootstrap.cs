using BigProject.Core.Match;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using System.Collections.Generic;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Point d'entrée de la scène de combat : crée le match, la session et relie tous les composants.
    /// Pour passer en ligne, il suffira de remplacer LocalMatchSession par une session réseau
    /// et de ne contrôler que le joueur local.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private void Start()
        {
            Camera mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (mainCamera == null)
            {
                Debug.LogError("GameBootstrap : aucune caméra dans la scène.");
                enabled = false;
                return;
            }

            // Logique de jeu (indépendante de Unity)
            var config = new MatchConfig();
            SpellCatalog spells = SpellCatalog.CreateDefault();
            MatchState state = MatchFactory.CreateDemo3v3(config, spells);
            var resolver = new RoundResolver(spells, new LastTeamStandingCondition());
            var session = new LocalMatchSession(state, spells, resolver);

            // Vues
            BoardView board = CreateChild<BoardView>("Board");
            board.Build(state.Map);

            TileHighlighter highlighter = CreateChild<TileHighlighter>("Highlights");
            highlighter.Init(board);

            UnitViewRegistry units = CreateChild<UnitViewRegistry>("Units");
            units.Build(state, board);

            // Entrées, planification, rejeu
            GridInput input = CreateChild<GridInput>("Input");
            input.Init(mainCamera, board);

            ResolutionPlayer resolution = CreateChild<ResolutionPlayer>("Resolution");
            resolution.Init(units, board, highlighter);
            session.RoundResolved += result => resolution.Play(result, state);

            // Test local : on contrôle tous les joueurs. En ligne : uniquement le joueur local.
            var localPlayers = new List<int>();
            foreach (UnitState unit in state.Units)
            {
                if (!localPlayers.Contains(unit.PlayerId))
                {
                    localPlayers.Add(unit.PlayerId);
                }
            }

            PlanningController planning = CreateChild<PlanningController>("Planning");
            planning.Init(session, board, highlighter, units, input, resolution, localPlayers);

            HudView hud = CreateChild<HudView>("Hud");
            hud.Init(planning, units, resolution);
            input.IsPointerBlocked = hud.ContainsScreenPoint;

            CameraRig rig = mainCamera.GetComponent<CameraRig>();
            if (rig == null)
            {
                rig = mainCamera.gameObject.AddComponent<CameraRig>();
            }

            rig.Init(state.Map, () => hud.PanelWidth);
        }

        private T CreateChild<T>(string objectName) where T : Component
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
