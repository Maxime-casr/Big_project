using System;
using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Session;
using BigProject.Core.Spells;
using BigProject.Core.Units;

namespace BigProject.Core.Net
{
    public sealed class ServerSettings
    {
        /// <summary>Un match démarre dès que 2 × PlayersPerTeam joueurs attendent (entre 1 et 4).</summary>
        public int PlayersPerTeam { get; set; } = 3;

        /// <summary>Durée de la phase de planification annoncée aux clients, 0 pour illimitée.</summary>
        public float PlanningSeconds { get; set; } = 45f;

        /// <summary>Marge ajoutée côté serveur pour laisser aux clients le temps d'envoyer leur plan final.</summary>
        public float GraceSeconds { get; set; } = 1.5f;
    }

    /// <summary>
    /// Serveur autoritaire : file d'attente, création des matchs, réception des plans, résolution
    /// et diffusion du résultat aux seuls joueurs du match concerné. Indépendant de Unity et du transport :
    /// appeler Update régulièrement et brancher un IServerTransport.
    /// </summary>
    public sealed class GameServer
    {
        private readonly IServerTransport transport;
        private readonly ServerSettings settings;
        private readonly SpellCatalog spells;
        private readonly RoundResolver resolver;
        private readonly Random random;
        private readonly Dictionary<int, ClientSlot> clients = new Dictionary<int, ClientSlot>();
        private readonly List<ClientSlot> queue = new List<ClientSlot>();
        private readonly List<Room> rooms = new List<Room>();

        public GameServer(IServerTransport transport, ServerSettings settings, SpellCatalog spells, Random random = null)
        {
            if (settings.PlayersPerTeam < 1 || settings.PlayersPerTeam > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "PlayersPerTeam doit être entre 1 et 4.");
            }

            this.transport = transport;
            this.settings = settings;
            this.spells = spells;
            this.random = random ?? new Random();
            resolver = new RoundResolver(spells, new LastTeamStandingCondition());

            transport.MessageReceived += OnMessage;
            transport.ClientDisconnected += OnDisconnected;
        }

        public int QueuedPlayerCount => queue.Count;
        public int ActiveMatchCount => rooms.Count;
        private int PlayersNeeded => settings.PlayersPerTeam * 2;

        /// <summary>À appeler à chaque frame : fait avancer les chronos de planification.</summary>
        public void Update(float deltaSeconds)
        {
            foreach (Room room in new List<Room>(rooms))
            {
                room.Match.Tick(deltaSeconds);
            }
        }

        private void OnMessage(int connectionId, byte[] data)
        {
            NetMessage message;
            try
            {
                message = MessageCodec.Decode(data);
            }
            catch (ProtocolException)
            {
                Kick(connectionId, "Message invalide.");
                return;
            }

            if (!clients.TryGetValue(connectionId, out ClientSlot slot))
            {
                slot = new ClientSlot { ConnectionId = connectionId };
                clients.Add(connectionId, slot);
            }

            switch (message)
            {
                case HelloMessage hello:
                    HandleHello(slot, hello);
                    break;
                case SubmitPlanMessage submit:
                    HandleSubmitPlan(slot, submit);
                    break;
                default:
                    Kick(connectionId, "Message inattendu.");
                    break;
            }
        }

        private void HandleHello(ClientSlot slot, HelloMessage hello)
        {
            if (slot.Room != null || queue.Contains(slot))
            {
                return;
            }

            if (hello.ProtocolVersion != NetProtocol.Version)
            {
                Kick(slot.ConnectionId, "Version du jeu incompatible.");
                return;
            }

            slot.Name = string.IsNullOrWhiteSpace(hello.PlayerName) ? "Joueur" : hello.PlayerName;
            queue.Add(slot);
            BroadcastQueueStatus();
            StartMatchesWhileEnoughPlayers();
        }

        private void HandleSubmitPlan(ClientSlot slot, SubmitPlanMessage submit)
        {
            if (slot.Room == null)
            {
                Send(slot, new PlanRejectedMessage("Aucun match en cours."));
                return;
            }

            // Le joueur est toujours déduit de la connexion, jamais du contenu du message.
            var plan = new TurnPlan(slot.PlayerId, submit.Units);
            if (!slot.Room.Match.TrySubmitPlan(plan, out string error))
            {
                Send(slot, new PlanRejectedMessage(error));
            }
        }

        private void StartMatchesWhileEnoughPlayers()
        {
            while (queue.Count >= PlayersNeeded)
            {
                var players = queue.GetRange(0, PlayersNeeded);
                queue.RemoveRange(0, PlayersNeeded);
                Shuffle(players);
                StartRoom(players);
                BroadcastQueueStatus();
            }
        }

        private void StartRoom(List<ClientSlot> players)
        {
            MatchState state = MatchFactory.CreateTeamMatch(new MatchConfig(), spells, settings.PlayersPerTeam);
            float timeout = settings.PlanningSeconds > 0f ? settings.PlanningSeconds + settings.GraceSeconds : 0f;
            var room = new Room { Match = new AuthoritativeMatch(state, resolver, timeout) };
            room.Match.RoundResolved += result => OnRoundResolved(room, result);

            for (int playerId = 0; playerId < players.Count; playerId++)
            {
                ClientSlot slot = players[playerId];
                slot.Room = room;
                slot.PlayerId = playerId;
                room.Clients.Add(slot);
            }

            rooms.Add(room);

            MatchSnapshot snapshot = MatchSnapshot.From(state);
            foreach (ClientSlot slot in room.Clients)
            {
                Send(slot, new MatchStartedMessage(slot.PlayerId, settings.PlanningSeconds, snapshot));
            }
        }

        private void OnRoundResolved(Room room, RoundResult result)
        {
            float planningSeconds = result.MatchEnded ? 0f : settings.PlanningSeconds;
            var message = new RoundResolvedMessage(result, planningSeconds, MatchSnapshot.From(room.Match.State));

            // Même contenu pour tous : visibilité totale. Pour un brouillard de guerre, il suffira
            // d'encoder ici un message filtré par joueur au lieu d'un message commun.
            byte[] data = MessageCodec.Encode(message);
            foreach (ClientSlot slot in room.Clients)
            {
                if (slot.Connected)
                {
                    transport.Send(slot.ConnectionId, data);
                }
            }

            if (result.MatchEnded)
            {
                CloseRoom(room, null);
            }
        }

        private void OnDisconnected(int connectionId)
        {
            if (!clients.TryGetValue(connectionId, out ClientSlot slot))
            {
                return;
            }

            clients.Remove(connectionId);
            slot.Connected = false;

            if (queue.Remove(slot))
            {
                BroadcastQueueStatus();
                return;
            }

            Room room = slot.Room;
            if (room == null || !rooms.Contains(room))
            {
                return;
            }

            room.Match.MarkDisconnected(slot.PlayerId);
            if (rooms.Contains(room) && !HasConnectedPlayerAlive(room))
            {
                CloseRoom(room, "Tous les joueurs actifs ont quitté le match.");
            }
        }

        private static bool HasConnectedPlayerAlive(Room room)
        {
            foreach (ClientSlot slot in room.Clients)
            {
                if (!slot.Connected)
                {
                    continue;
                }

                foreach (UnitState unit in room.Match.State.Units)
                {
                    if (unit.IsAlive && unit.PlayerId == slot.PlayerId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CloseRoom(Room room, string errorForRemainingClients)
        {
            rooms.Remove(room);
            foreach (ClientSlot slot in room.Clients)
            {
                slot.Room = null;
                if (slot.Connected && errorForRemainingClients != null)
                {
                    Send(slot, new ServerErrorMessage(errorForRemainingClients));
                }
            }
        }

        private void BroadcastQueueStatus()
        {
            foreach (ClientSlot slot in queue)
            {
                Send(slot, new QueueStatusMessage(queue.Count, PlayersNeeded));
            }
        }

        private void Send(ClientSlot slot, NetMessage message)
        {
            transport.Send(slot.ConnectionId, MessageCodec.Encode(message));
        }

        private void Kick(int connectionId, string reason)
        {
            transport.Send(connectionId, MessageCodec.Encode(new ServerErrorMessage(reason)));
            transport.Disconnect(connectionId);
        }

        private void Shuffle(List<ClientSlot> players)
        {
            for (int i = players.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                ClientSlot temp = players[i];
                players[i] = players[j];
                players[j] = temp;
            }
        }

        private sealed class ClientSlot
        {
            public int ConnectionId;
            public string Name;
            public Room Room;
            public int PlayerId;
            public bool Connected = true;
        }

        private sealed class Room
        {
            public AuthoritativeMatch Match;
            public readonly List<ClientSlot> Clients = new List<ClientSlot>();
        }
    }
}
