using System;
using System.Collections.Generic;
using BigProject.Core.Characters;
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
        public float PlanningSeconds { get; set; } = 20f;

        /// <summary>Marge ajoutée côté serveur pour laisser aux clients le temps d'envoyer leur plan final.</summary>
        public float GraceSeconds { get; set; } = 1.5f;

        /// <summary>Carte des matchs ; null pour la carte par défaut du jeu.</summary>
        public MapDefinition Map { get; set; }
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
        private readonly CharacterCatalog characters;
        private readonly int contentHash;
        private readonly RoundResolver resolver;
        private readonly Random random;
        private readonly Dictionary<int, ClientSlot> clients = new Dictionary<int, ClientSlot>();
        private readonly List<ClientSlot> queue = new List<ClientSlot>();
        private readonly List<Room> rooms = new List<Room>();
        private double time;

        /// <summary>Intervalle minimal entre deux brouillons d'un même joueur (anti-spam).</summary>
        private const double MinDraftInterval = 0.05;

        /// <param name="characters">Les personnages du jeu ; par défaut, les personnages d'exemple.</param>
        public GameServer(
            IServerTransport transport,
            ServerSettings settings,
            SpellCatalog spells,
            Random random = null,
            CharacterCatalog characters = null)
        {
            if (settings.PlayersPerTeam < 1 || settings.PlayersPerTeam > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(settings), "PlayersPerTeam doit être entre 1 et 4.");
            }

            this.transport = transport;
            this.settings = settings;
            this.spells = spells;
            this.characters = characters ?? CharacterCatalog.CreateDefault();
            if (this.characters.PlayableIds.Count == 0)
            {
                throw new ArgumentException("Le serveur n'a aucun personnage jouable.", nameof(characters));
            }

            contentHash = ContentRules.ComputeHash(spells, this.characters);
            this.random = random ?? new Random();
            // Le hasard de la résolution (priorité en cas de collision) vient du serveur, jamais d'un client.
            resolver = new RoundResolver(spells, new LastTeamStandingCondition(), new Random(this.random.Next()), this.characters);

            transport.MessageReceived += OnMessage;
            transport.ClientDisconnected += OnDisconnected;
        }

        public int QueuedPlayerCount => queue.Count;
        public int ActiveMatchCount => rooms.Count;
        private int PlayersNeeded => settings.PlayersPerTeam * 2;

        /// <summary>À appeler à chaque frame : fait avancer les chronos de planification.</summary>
        public void Update(float deltaSeconds)
        {
            time += deltaSeconds;
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
                case DraftMessage draft:
                    HandleDraft(slot, draft);
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

            if (hello.ContentHash != contentHash)
            {
                Kick(slot.ConnectionId, "Les sorts ou personnages du client et du serveur diffèrent. Mets les deux à jour.");
                return;
            }

            // Le personnage est choisi par le client, mais c'est le serveur qui décide : inconnu ou non jouable
            // (une invocation, par exemple) est refusé. Sans choix, le premier personnage jouable est attribué.
            string wanted = hello.CharacterId;
            if (string.IsNullOrEmpty(wanted))
            {
                wanted = characters.PlayableIds[0];
            }

            CharacterDefinition character = characters.Get(wanted);
            if (character == null || !character.IsPlayable)
            {
                Kick(slot.ConnectionId, "Personnage inconnu ou non jouable.");
                return;
            }

            slot.CharacterId = character.Id;
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
            Room room = slot.Room;
            int round = room.Match.State.RoundNumber;
            var plan = new TurnPlan(slot.PlayerId, submit.Units);
            if (!room.Match.TrySubmitPlan(plan, out string error))
            {
                Send(slot, new PlanRejectedMessage(error));
                return;
            }

            BroadcastPlanStatus(room, round);
        }

        /// <summary>
        /// Relaie le brouillon d'un joueur à ses alliés, jamais aux ennemis. Seules les entrées valides,
        /// pour des unités du joueur, sont relayées.
        /// </summary>
        private void HandleDraft(ClientSlot slot, DraftMessage draft)
        {
            Room room = slot.Room;
            if (room == null || room.Match.State.Phase != MatchPhase.Planning ||
                room.Match.HasSubmitted(slot.PlayerId) || time - slot.LastDraftTime < MinDraftInterval)
            {
                return;
            }

            slot.LastDraftTime = time;
            MatchState state = room.Match.State;
            var valid = new List<UnitPlan>();
            int team = MatchState.NoTeam;

            foreach (UnitPlan unitPlan in draft.Units)
            {
                UnitState unit = state.GetUnit(unitPlan.UnitId);
                if (unit == null || unit.PlayerId != slot.PlayerId || !unit.IsAlive)
                {
                    continue;
                }

                if (PlanValidator.Project(state, spells, unit, unitPlan.Orders).IsValid)
                {
                    valid.Add(unitPlan);
                    team = unit.TeamId;
                }
            }

            if (valid.Count == 0)
            {
                return;
            }

            byte[] data = MessageCodec.Encode(new AllyDraftMessage(valid));
            foreach (ClientSlot other in room.Clients)
            {
                if (other != slot && other.Connected && TeamOf(state, other.PlayerId) == team)
                {
                    transport.Send(other.ConnectionId, data);
                }
            }
        }

        private static int TeamOf(MatchState state, int playerId)
        {
            foreach (UnitState unit in state.Units)
            {
                if (unit.PlayerId == playerId)
                {
                    return unit.TeamId;
                }
            }

            return MatchState.NoTeam;
        }

        /// <summary>Informe le match du nombre de joueurs prêts, si le tour n'a pas été résolu entre-temps.</summary>
        private void BroadcastPlanStatus(Room room, int round)
        {
            if (!rooms.Contains(room) || room.Match.State.Phase != MatchPhase.Planning ||
                room.Match.State.RoundNumber != round)
            {
                return;
            }

            byte[] data = MessageCodec.Encode(
                new PlanStatusMessage(room.Match.ReadyPlayerCount, room.Match.ExpectedPlayerCount));
            foreach (ClientSlot client in room.Clients)
            {
                if (client.Connected)
                {
                    transport.Send(client.ConnectionId, data);
                }
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
            var characterIds = new List<string>(players.Count);
            foreach (ClientSlot player in players)
            {
                characterIds.Add(player.CharacterId);
            }

            MatchState state = MatchFactory.CreateTeamMatch(
                new MatchConfig(), characters, settings.PlayersPerTeam, settings.Map, characterIds);
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

            int roundBeforeDisconnect = room.Match.State.RoundNumber;
            room.Match.MarkDisconnected(slot.PlayerId);
            if (rooms.Contains(room) && !HasConnectedPlayerAlive(room))
            {
                CloseRoom(room, "Tous les joueurs actifs ont quitté le match.");
            }
            else
            {
                BroadcastPlanStatus(room, roundBeforeDisconnect);
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
            public string CharacterId;
            public Room Room;
            public int PlayerId;
            public double LastDraftTime = -1;
            public bool Connected = true;
        }

        private sealed class Room
        {
            public AuthoritativeMatch Match;
            public readonly List<ClientSlot> Clients = new List<ClientSlot>();
        }
    }
}
