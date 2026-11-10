using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Net;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Point d'entrée de la scène de combat. Utilise la session en ligne préparée par le menu ;
    /// sans elle (par exemple en lançant directement cette scène), démarre un combat local de test
    /// où un seul poste contrôle tous les joueurs.
    /// </summary>
    public sealed class CombatBootstrap : MonoBehaviour
    {
        [Tooltip("Durée de planification d'un tour pour le combat local de test.")]
        [SerializeField] private float planningSeconds = 20f;

        private NetworkMatchSession onlineSession;
        private LocalMatchSession localSession;
        private bool leaveRequested;
        private string leaveMessage;

        private void Start()
        {
            Camera camera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("CombatBootstrap : aucune caméra dans la scène.");
                enabled = false;
                return;
            }

            IMatchSession session;
            IEnumerable<int> localPlayers;

            onlineSession = GameFlow.OnlineSession;
            if (onlineSession != null && onlineSession.IsInMatch)
            {
                session = onlineSession;
                localPlayers = new[] { onlineSession.LocalPlayerId };
                onlineSession.Failed += OnNetworkFailed;
            }
            else
            {
                onlineSession = null;
                session = CreateLocalTestSession(out localPlayers);
            }

            MatchScene scene = MatchScene.Create(transform, camera, session, localPlayers);
            scene.QuitRequested += () => GameFlow.ReturnToMenu();
        }

        private void Update()
        {
            // Hors ligne, c'est ici que le chrono de planification avance.
            localSession?.Tick(Time.unscaledDeltaTime);

            if (leaveRequested)
            {
                leaveRequested = false;
                GameFlow.ReturnToMenu(leaveMessage);
            }
        }

        private void OnDestroy()
        {
            if (onlineSession != null)
            {
                onlineSession.Failed -= OnNetworkFailed;
            }
        }

        private void OnNetworkFailed(string reason)
        {
            // Différé : on ne démonte pas Mirror depuis l'un de ses propres callbacks.
            leaveRequested = true;
            leaveMessage = reason;
        }

        private IMatchSession CreateLocalTestSession(out IEnumerable<int> localPlayers)
        {
            SpellCatalog spells = SpellCatalog.CreateDefault();
            MatchState match = MatchFactory.CreateDemo3v3(new MatchConfig(), spells);
            var resolver = new RoundResolver(spells, new LastTeamStandingCondition());

            var players = new List<int>();
            foreach (UnitState unit in match.Units)
            {
                if (!players.Contains(unit.PlayerId))
                {
                    players.Add(unit.PlayerId);
                }
            }

            localPlayers = players;
            localSession = new LocalMatchSession(match, spells, resolver, planningSeconds, 1f);
            return localSession;
        }
    }
}
