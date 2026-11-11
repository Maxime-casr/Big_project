using BigProject.Core.Characters;
using BigProject.Core.Net;
using BigProject.Core.Spells;
using kcp2k;
using Mirror;
using UnityEngine;

namespace BigProject.Online
{
    /// <summary>
    /// Démarre et arrête Mirror (transport KCP) pour le rôle de serveur ou de client, et fait avancer
    /// le GameServer / la NetworkMatchSession chaque frame.
    /// </summary>
    public sealed class MirrorNetworkRunner : MonoBehaviour
    {
        private NetworkManager manager;
        private MirrorServerTransport serverTransport;
        private MirrorClientTransport clientTransport;
        private GameServer server;
        private NetworkMatchSession clientSession;

        public bool IsServerRunning => server != null;
        public int QueuedPlayerCount => server?.QueuedPlayerCount ?? 0;
        public int ActiveMatchCount => server?.ActiveMatchCount ?? 0;

        public void StartServer(ushort port, ServerSettings settings, SpellCatalog spells, CharacterCatalog characters)
        {
            Shutdown();
            CreateManager(port);
            manager.StartServer();

            serverTransport = new MirrorServerTransport();
            server = new GameServer(serverTransport, settings, spells, null, characters);
        }

        public NetworkMatchSession StartClient(
            string address, ushort port, string playerName, string characterId, SpellCatalog spells, CharacterCatalog characters)
        {
            Shutdown();
            CreateManager(port);
            manager.networkAddress = address;
            manager.StartClient();

            clientTransport = new MirrorClientTransport();
            clientSession = new NetworkMatchSession(clientTransport, spells, playerName, characterId, characters);
            return clientSession;
        }

        public void Shutdown()
        {
            serverTransport?.Dispose();
            clientTransport?.Dispose();
            serverTransport = null;
            clientTransport = null;
            server = null;
            clientSession = null;

            if (manager != null)
            {
                if (NetworkServer.active)
                {
                    manager.StopServer();
                }

                if (NetworkClient.active)
                {
                    manager.StopClient();
                }

                Destroy(manager.gameObject);
                manager = null;
            }
        }

        private void Update()
        {
            server?.Update(Time.unscaledDeltaTime);
            clientSession?.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void CreateManager(ushort port)
        {
            // Créé désactivé pour que le transport et les réglages soient en place avant l'Awake du NetworkManager.
            var go = new GameObject("NetworkManager");
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            var transport = go.AddComponent<KcpTransport>();
            transport.Port = port;

            manager = go.AddComponent<NetworkManager>();
            manager.transport = transport;
            manager.autoCreatePlayer = false;
            manager.dontDestroyOnLoad = false;
            manager.runInBackground = true;
            manager.maxConnections = 200;

            go.SetActive(true);
        }
    }
}
