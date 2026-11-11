using BigProject.Core.Net;
using BigProject.Online;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BigProject.Client
{
    /// <summary>
    /// Passage entre la scène de menu et la scène de combat. Garde ce qui doit survivre au changement
    /// de scène : la connexion réseau et la session en ligne.
    /// </summary>
    public static class GameFlow
    {
        public const string MenuSceneName = "Menue";
        public const string CombatSceneName = "Combat";

        private static MirrorNetworkRunner runner;

        /// <summary>Session en ligne à utiliser par la scène de combat. Null : combat local de test.</summary>
        public static NetworkMatchSession OnlineSession { get; private set; }

        /// <summary>Message à afficher au retour dans le menu (erreur de connexion, par exemple).</summary>
        public static string MenuMessage { get; private set; } = string.Empty;

        /// <summary>Gestionnaire Mirror, créé à la demande et conservé entre les scènes.</summary>
        public static MirrorNetworkRunner Runner
        {
            get
            {
                if (runner == null)
                {
                    var go = new GameObject("Network");
                    Object.DontDestroyOnLoad(go);
                    runner = go.AddComponent<MirrorNetworkRunner>();
                }

                return runner;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            runner = null;
            OnlineSession = null;
            MenuMessage = string.Empty;
            LocalTestCharacterId = string.Empty;
        }

        /// <summary>Personnage choisi au menu pour le combat local : il est le premier de l'équipe bleue.</summary>
        public static string LocalTestCharacterId { get; private set; } = string.Empty;

        public static void StartLocalTest(string characterId)
        {
            LocalTestCharacterId = characterId ?? string.Empty;
            OnlineSession = null;
            SceneManager.LoadScene(CombatSceneName);
        }

        public static void EnterOnlineMatch(NetworkMatchSession session)
        {
            OnlineSession = session;
            SceneManager.LoadScene(CombatSceneName);
        }

        public static void ReturnToMenu(string message = null)
        {
            OnlineSession = null;
            if (runner != null)
            {
                runner.Shutdown();
                Object.Destroy(runner.gameObject);
                runner = null;
            }

            MenuMessage = message ?? string.Empty;
            SceneManager.LoadScene(MenuSceneName);
        }
    }
}
