using System;
using BigProject.Core.Net;
using BigProject.Core.Spells;
using UnityEngine;

namespace BigProject.Client
{
    /// <summary>
    /// Scène de menu : combat local de test, recherche d'une partie en ligne, ou lancement d'un serveur
    /// (aussi automatique avec l'argument -server ou en batchmode).
    /// Arguments du serveur : -port 7777 -playersPerTeam 3 -planningSeconds 20
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        private enum MenuState
        {
            Menu,
            Connecting,
            Server
        }

        private MenuState state = MenuState.Menu;
        private string address = "localhost";
        private string portText = "7777";
        private string playerName = "Joueur";
        private string playersPerTeamText = "3";
        private string message = string.Empty;
        private string queueMessage = string.Empty;

        private NetworkMatchSession onlineSession;
        private bool returnToMenuRequested;
        private string returnToMenuMessage;

        private void Start()
        {
            message = GameFlow.MenuMessage;

            if (Application.isBatchMode || HasArgument("-server"))
            {
                StartServer(
                    ArgumentValue("-port", 7777),
                    ArgumentValue("-playersPerTeam", 3),
                    ArgumentValue("-planningSeconds", 20));
            }
        }

        private void Update()
        {
            if (returnToMenuRequested)
            {
                returnToMenuRequested = false;
                GameFlow.ReturnToMenu(returnToMenuMessage);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void OnGUI()
        {
            switch (state)
            {
                case MenuState.Menu:
                    DrawMenu();
                    break;
                case MenuState.Connecting:
                    DrawBox(() =>
                    {
                        GUILayout.Label(string.IsNullOrEmpty(queueMessage) ? "Connexion au serveur..." : queueMessage);
                        if (GUILayout.Button("Annuler"))
                        {
                            GameFlow.ReturnToMenu();
                        }
                    });
                    break;
                case MenuState.Server:
                    DrawBox(() =>
                    {
                        GUILayout.Label("<b>Serveur en fonctionnement</b>", RichLabel());
                        GUILayout.Label("Port " + portText + " — " + playersPerTeamText + " joueur(s) par équipe");
                        GUILayout.Label("File d'attente : " + GameFlow.Runner.QueuedPlayerCount);
                        GUILayout.Label("Matchs en cours : " + GameFlow.Runner.ActiveMatchCount);
                        if (GUILayout.Button("Arrêter le serveur"))
                        {
                            GameFlow.ReturnToMenu();
                        }
                    });
                    break;
            }
        }

        private void DrawMenu()
        {
            DrawBox(() =>
            {
                GUILayout.Label("<b>Big Project — tour par tour simultané</b>", RichLabel());
                if (!string.IsNullOrEmpty(message))
                {
                    GUILayout.Label(message);
                }

                GUILayout.Space(8f);
                if (GUILayout.Button("Combat local (test)", GUILayout.Height(36f)))
                {
                    GameFlow.StartLocalTest();
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
                    StartServer(ParseInt(portText, 7777), ParseInt(playersPerTeamText, 3), 20);
                }
            });
        }

        private void ConnectOnline()
        {
            if (!ushort.TryParse(portText, out ushort port))
            {
                message = "Port invalide.";
                return;
            }

            queueMessage = string.Empty;
            onlineSession = GameFlow.Runner.StartClient(address, port, playerName, SpellCatalog.CreateDefault());
            onlineSession.QueueChanged += OnQueueChanged;
            onlineSession.MatchStarted += OnMatchStarted;
            onlineSession.Failed += OnNetworkFailed;
            state = MenuState.Connecting;
        }

        private void StartServer(int port, int playersPerTeam, int planningSeconds)
        {
            if (port < 1 || port > ushort.MaxValue || playersPerTeam < 1 || playersPerTeam > 4)
            {
                message = "Paramètres de serveur invalides.";
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

            GameFlow.Runner.StartServer((ushort)port, settings, SpellCatalog.CreateDefault());
            state = MenuState.Server;
            Debug.Log("Serveur démarré sur le port " + port + " (" + playersPerTeam + " joueur(s) par équipe).");
        }

        private void OnQueueChanged(int waiting, int needed)
        {
            queueMessage = "File d'attente : " + waiting + " / " + needed + " joueurs.";
        }

        private void OnMatchStarted()
        {
            NetworkMatchSession session = onlineSession;
            Unsubscribe();
            GameFlow.EnterOnlineMatch(session);
        }

        private void OnNetworkFailed(string reason)
        {
            // Différé : on ne démonte pas Mirror depuis l'un de ses propres callbacks.
            returnToMenuRequested = true;
            returnToMenuMessage = reason;
        }

        private void Unsubscribe()
        {
            if (onlineSession == null)
            {
                return;
            }

            onlineSession.QueueChanged -= OnQueueChanged;
            onlineSession.MatchStarted -= OnMatchStarted;
            onlineSession.Failed -= OnNetworkFailed;
            onlineSession = null;
        }

        private static void DrawBox(Action content)
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
