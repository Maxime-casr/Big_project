using System;
using System.Collections.Generic;
using BigProject.Core.Match;
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

        private const string CharacterPrefKey = "BigProject.Character";

        private MenuState state = MenuState.Menu;
        private List<GameContent.CharacterInfo> characterChoices = new List<GameContent.CharacterInfo>();
        private int characterIndex;
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
            LoadCharacterChoices();

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

        private void LoadCharacterChoices()
        {
            characterChoices = GameContent.GetPlayableCharacters();
            string saved = PlayerPrefs.GetString(CharacterPrefKey, string.Empty);
            characterIndex = Mathf.Max(0, characterChoices.FindIndex(c => c.Id == saved));
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
                DrawCharacterPicker();
                GUILayout.Space(4f);
                if (GUILayout.Button("Combat local (test)", GUILayout.Height(36f)))
                {
                    GameFlow.StartLocalTest(SelectedCharacterId);
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

        private string SelectedCharacterId =>
            characterChoices.Count == 0 ? string.Empty : characterChoices[characterIndex].Id;

        /// <summary>Le choix du personnage : deux flèches, son nom, ses stats et ses sorts.</summary>
        private void DrawCharacterPicker()
        {
            if (characterChoices.Count == 0)
            {
                GUILayout.Label("Aucun personnage jouable (voir Assets/Resources/Characters).");
                return;
            }

            GUILayout.Label("<b>Personnage</b>", RichLabel());
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(36f), GUILayout.Height(28f)))
            {
                ChangeCharacter(-1);
            }

            GUILayout.Label("<b>" + characterChoices[characterIndex].Name + "</b>", CenteredLabel(), GUILayout.Height(28f));
            if (GUILayout.Button(">", GUILayout.Width(36f), GUILayout.Height(28f)))
            {
                ChangeCharacter(1);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label(characterChoices[characterIndex].Summary);
        }

        private void ChangeCharacter(int step)
        {
            characterIndex = (characterIndex + step + characterChoices.Count) % characterChoices.Count;
            PlayerPrefs.SetString(CharacterPrefKey, characterChoices[characterIndex].Id);
        }

        private static GUIStyle CenteredLabel()
        {
            return new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true };
        }

        private void ConnectOnline()
        {
            if (!ushort.TryParse(portText, out ushort port))
            {
                message = "Port invalide.";
                return;
            }

            queueMessage = string.Empty;
            onlineSession = GameFlow.Runner.StartClient(
                address, port, playerName, SelectedCharacterId, GameContent.LoadSpells(), GameContent.LoadCharacters());
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
                PlanningSeconds = planningSeconds,
                Map = LoadServerMap()
            };

            GameFlow.Runner.StartServer((ushort)port, settings, GameContent.LoadSpells(), GameContent.LoadCharacters());
            state = MenuState.Server;
            Debug.Log("Serveur démarré sur le port " + port + " (" + playersPerTeam + " joueur(s) par équipe).");
        }

        /// <summary>Carte du serveur : l'asset généré depuis la Tilemap, à défaut la carte par défaut du jeu.</summary>
        private static MapDefinition LoadServerMap()
        {
            MapData data = MapData.Load();
            MapDefinition map = data != null ? data.ToDefinition() : null;
            if (map == null)
            {
                Debug.LogWarning(
                    "Aucune carte valide dans Resources/" + MapData.ResourcePath + " : le serveur utilise la carte par défaut. " +
                    "Ouvre la scène de combat et enregistre-la (ou menu Big Project > Générer la carte du serveur).");
            }

            return map;
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
