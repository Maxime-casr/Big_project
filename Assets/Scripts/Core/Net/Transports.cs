using System;

namespace BigProject.Core.Net
{
    /// <summary>
    /// Le serveur ne connaît que cette interface : Mirror, un autre transport ou un simulateur de test
    /// peuvent l'implémenter. Les messages sont des octets déjà encodés par MessageCodec.
    /// </summary>
    public interface IServerTransport
    {
        /// <summary>Un client envoie un message (connectionId, octets non fiables).</summary>
        event Action<int, byte[]> MessageReceived;

        event Action<int> ClientDisconnected;

        void Send(int connectionId, byte[] data);

        void Disconnect(int connectionId);
    }

    public interface IClientTransport
    {
        event Action Connected;
        event Action Disconnected;
        event Action<byte[]> MessageReceived;

        void Send(byte[] data);
    }
}
