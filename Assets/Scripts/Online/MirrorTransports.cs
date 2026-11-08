using System;
using BigProject.Core.Net;
using Mirror;

namespace BigProject.Online
{
    /// <summary>
    /// Seul type de message envoyé via Mirror : un paquet d'octets déjà encodé par MessageCodec.
    /// Aucun NetworkIdentity ni objet synchronisé : tout l'état de jeu passe par le protocole du Core.
    /// </summary>
    public struct PacketMessage : NetworkMessage
    {
        public byte[] Data;
    }

    public sealed class MirrorServerTransport : IServerTransport, IDisposable
    {
        public MirrorServerTransport()
        {
            // À créer après NetworkManager.StartServer : le manager réinitialise ces callbacks au démarrage.
            NetworkServer.RegisterHandler<PacketMessage>(OnPacket, false);
            NetworkServer.OnDisconnectedEvent += OnDisconnected;
        }

        public event Action<int, byte[]> MessageReceived;
        public event Action<int> ClientDisconnected;

        public void Send(int connectionId, byte[] data)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient connection))
            {
                connection.Send(new PacketMessage { Data = data });
            }
        }

        public void Disconnect(int connectionId)
        {
            if (NetworkServer.connections.TryGetValue(connectionId, out NetworkConnectionToClient connection))
            {
                connection.Disconnect();
            }
        }

        public void Dispose()
        {
            NetworkServer.UnregisterHandler<PacketMessage>();
            NetworkServer.OnDisconnectedEvent -= OnDisconnected;
        }

        private void OnPacket(NetworkConnectionToClient connection, PacketMessage message)
        {
            MessageReceived?.Invoke(connection.connectionId, message.Data);
        }

        private void OnDisconnected(NetworkConnectionToClient connection)
        {
            ClientDisconnected?.Invoke(connection.connectionId);
        }
    }

    public sealed class MirrorClientTransport : IClientTransport, IDisposable
    {
        public MirrorClientTransport()
        {
            // À créer après NetworkManager.StartClient : le manager réinitialise ces callbacks au démarrage.
            NetworkClient.RegisterHandler<PacketMessage>(OnPacket, false);
            NetworkClient.OnConnectedEvent += OnConnected;
            NetworkClient.OnDisconnectedEvent += OnDisconnected;
        }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> MessageReceived;

        public void Send(byte[] data)
        {
            NetworkClient.Send(new PacketMessage { Data = data });
        }

        public void Dispose()
        {
            NetworkClient.UnregisterHandler<PacketMessage>();
            NetworkClient.OnConnectedEvent -= OnConnected;
            NetworkClient.OnDisconnectedEvent -= OnDisconnected;
        }

        private void OnPacket(PacketMessage message)
        {
            MessageReceived?.Invoke(message.Data);
        }

        private void OnConnected()
        {
            Connected?.Invoke();
        }

        private void OnDisconnected()
        {
            Disconnected?.Invoke();
        }
    }
}
