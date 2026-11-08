using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Net;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using BigProject.Online;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Point d'entrée de la scène. Trois rôles :
    /// - jeu hors ligne (test local, un seul poste contrôle tous les joueurs) ;
    /// - client en ligne (file d'attente, puis match sur le serveur) ;
    /// - serveur (menu "Lancer un serveur", ou automatiquement avec l'argument -server / en batchmode).
    /// Arguments du serveur : -port 7777 -playersPerTeam 3 -planningSeconds 45
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        private enum AppState
        {
            Menu,
            Connecting,
            InMatch,
            Server
        }

        private AppState state = AppState.Menu;
        private string address = "localhost";
        private string portText = "7777";
        private string playerName = "Joueur";
        private string playersPerTeamText = "3";
        private string menuMessage = string.Empty;
        private string queueMessage = string.Empty;

        private MirrorNetworkRunner runner;
        private NetworkMatchSession onlineSession;
        private MatchScene matchScene;
        private bool returnToMenuRequested;
        private string returnToMenuMessage;

        private void Start()
        {
            if (Application.isBatchMode || HasArgument("-server"))
            {
                StartServer(
                    ArgumentValue("-port", 7777),
                    ArgumentValue("-playersPerTeam", 3),
                    ArgumentValue("-planningSeconds", 45));
            }
        }

        private void Update()
        {
            if (returnToMenuRequested)
            {
                returnToMenuRequested = false;
                ReturnToMenu(returnToMenuMessage);
            }
        }

        private void OnGUI()
        {
            switch (state)
            {
                case AppState.Menu:
                    DrawMenu();
                    break;
                case AppState.Connecting:
                    DrawCenteredBox(() =>
                    {
                        GUILayout.Label(string.IsNullOrEmpty(queueMessage) ? "Connexion au serveur..." : queueMessage);
                        if (GUILayout.Button("Annuler"))
                        {
                            ReturnToMenu(null);
                        }
                    });
                    break;
                case AppState.Server:
                    DrawServerStatus();
                    break;
            }
        }

        private void DrawMenu()
        {
            DrawCenteredBox(() =>
            {
                GUILayout.Label("<b>Big Project — tour par tour simultané</b>", RichLabel());
                if (!string.IsNullOrEmpty(menuMessage))
                {
                    GUILayout.Label(menuMessage);
                }

                GUILayout.Space(8f);
                if (GUILayout.Button("Jouer hors ligne (test local)", GUILayout.Height(32f)))
                {
                    StartOffline();
                }

                GUILayout.Space(10f);
                GUILayout.Label("<b>En ligne</b>", RichLabel());
                playerName = LabeledField("Pseudo", playerName);
                address = LabeledField("Adresse du serveur", address);
                portText = LabeledField("Port", portText);
                if (GUILayout.Button("Chercher une partie", GUILayout.Height(32f)))
                {
                    ConnectOnline();
                }

                GUILayout.Space(10f);
                GUILayout.Label("<b>Serveur</b>", RichLabel());
                playersPerTeamText = LabeledField("Joueurs par équipe (1 à 4)", playersPerTeamText);
                if (GUILayout.Button("Lancer un serveur ici"))
                {
                    StartServer(ParseInt(portText, 7777), ParseInt(playersPerTeamText, 3), 45);
                }
            });
        }

        private void DrawServerStatus()
        {
            DrawCenteredBox(() =>
            {
                GUILayout.Label("<b>Serveur en fonctionnement</b>", RichLabel());
                GUILayout.Label("Port " + portText + " — " + playersPerTeamText + " joueur(s) par équipe");
                GUILayout.Label("File d'attente : " + Runner.QueuedPlayerCount);
                GUILayout.Label("Matchs en cours : " + Runner.ActiveMatchCount);
                if (GUILayout.Button("Arrêter le serveur"))
                {
                    ReturnToMenu(null);
                }
            });
        }

        private void StartOffline()
        {
            var config = new MatchConfig();
            SpellCatalog spells = SpellCatalog.CreateDefault();
            MatchState match = MatchFactory.CreateDemo3v3(config, spells);
            var resolver = new RoundResolver(spells, new LastTeamStandingCondition());
            var session = new LocalMatchSession(match, spells, resolver);

            // Test local : un seul poste contrôle tous les joueurs.
            var localPlayers = new List<int>();
            foreach (UnitState unit in match.Units)
            {
                if (!localPlayers.Contains(unit.PlayerId))
                {
                    localPlayers.Add(unit.PlayerId);
                }
            }

            EnterMatch(session, localPlayers);
        }

        private void ConnectOnline()
        {
            if (!ushort.TryParse(portText, out ushort port))
            {
                menuMessage = "Port invalide.";
                return;
            }

            queueMessage = string.Empty;
            onlineSession = Runner.StartClient(address, port, playerName, SpellCatalog.CreateDefault());
            onlineSession.QueueChanged += OnQueueChanged;
            onlineSession.MatchStarted += OnMatchStarted;
            onlineSession.Failed += OnNetworkFailed;
            state = AppState.Connecting;
        }

        private void StartServer(int port, int playersPerTeam, int planningSeconds)
        {
            if (port < 1 || port > ushort.MaxValue || playersPerTeam < 1 || playersPerTeam > 4)
            {
                menuMessage = "Paramètres de serveur invalides.";
                return;
            }

            portText = port.ToString();
            playersPerTeamText = playersPerTeam.ToString();
            Application.targetFrameRate = 30;

            var settings = new ServerSettings
            {
                PlayersPerTeam = playersPerTeam,
                PlanningSeconds = planningSeconds
            };

            Runner.StartServer((ushort)port, settings, SpellCatalog.CreateDefault());
            state = AppState.Server;
            Debug.Log("Serveur démarré sur le port " + port + " (" + playersPerTeam + " joueur(s) par équipe).");
        }

        private void OnQueueChanged(int waiting, int needed)
        {
            queueMessage = "File d'attente : " + waiting + " / " + needed + " joueurs.";
        }

        private void OnMatchStarted()
        {
            EnterMatch(onlineSession, new[] { onlineSession.LocalPlayerId });
        }

        private void OnNetworkFailed(string reason)
        {
            // Différé : on ne démonte pas Mirror depuis l'un de ses propres callbacks.
            returnToMenuRequested = true;
            returnToMenuMessage = reason;
        }

        private void EnterMatch(IMatchSession session, IEnumerable<int> localPlayerIds)
        {
            Camera camera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("GameBootstrap : aucune caméra dans la scène.");
                return;
            }

            matchScene = MatchScene.Create(transform, camera, session, localPlayerIds);
            matchScene.QuitRequested += () =>
            {
                returnToMenuRequested = true;
                returnToMenuMessage = null;
            };
            state = AppState.InMatch;
        }

        private void ReturnToMenu(string message)
        {
            if (onlineSession != null)
            {
                onlineSession.QueueChanged -= OnQueueChanged;
                onlineSession.MatchStarted -= OnMatchStarted;
                onlineSession.Failed -= OnNetworkFailed;
                onlineSession = null;
            }

            if (runner != null)
            {
                runner.Shutdown();
            }

            if (matchScene != null)
            {
                Destroy(matchScene.gameObject);
                matchScene = null;
            }

            menuMessage = message ?? string.Empty;
            state = AppState.Menu;
        }

        private MirrorNetworkRunner Runner
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("Network");
                    go.transform.SetParent(transform, false);
                    runner = go.AddComponent<MirrorNetworkRunner>();
                }

                return runner;
            }
        }

        private static void DrawCenteredBox(Action content)
        {
            const float width = 380f;
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, 40f, width, Screen.height - 80f), GUI.skin.box);
            content();
            GUILayout.EndArea();
        }

        private static string LabeledField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170f));
            string result = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
            return result;
        }

        private static GUIStyle RichLabel()
        {
            return new GUIStyle(GUI.skin.label) { richText = true };
        }

        private static bool HasArgument(string name)
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
        }

        private static int ArgumentValue(string name, int fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? ParseInt(args[index + 1], fallback) : fallback;
        }

        private static int ParseInt(string text, int fallback)
        {
            return int.TryParse(text, out int value) ? value : fallback;
        }
    }
}
