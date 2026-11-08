using System;
using System.Collections.Generic;
using BigProject.Core.Grid;
using BigProject.Core.Match;
using BigProject.Core.Net;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;
using BigProject.Core.Spells;
using BigProject.Core.Units;
using NUnit.Framework;

namespace BigProject.Tests
{
    /// <summary>Réseau en mémoire : relie des clients à un GameServer sans aucun socket.</summary>
    internal sealed class LoopbackNetwork : IServerTransport
    {
        private readonly Dictionary<int, LoopbackClient> clients = new Dictionary<int, LoopbackClient>();
        private int nextId = 1;

        public event Action<int, byte[]> MessageReceived;
        public event Action<int> ClientDisconnected;

        public List<int> DisconnectedByServer { get; } = new List<int>();

        public LoopbackClient CreateClient()
        {
            var client = new LoopbackClient(this, nextId++);
            clients.Add(client.Id, client);
            return client;
        }

        public void Send(int connectionId, byte[] data)
        {
            if (clients.TryGetValue(connectionId, out LoopbackClient client))
            {
                client.Deliver(data);
            }
        }

        public void Disconnect(int connectionId)
        {
            DisconnectedByServer.Add(connectionId);
            if (clients.Remove(connectionId))
            {
                ClientDisconnected?.Invoke(connectionId);
            }
        }

        internal void FromClient(int id, byte[] data)
        {
            MessageReceived?.Invoke(id, data);
        }

        internal void ClientLeft(int id)
        {
            if (clients.Remove(id))
            {
                ClientDisconnected?.Invoke(id);
            }
        }
    }

    internal sealed class LoopbackClient : IClientTransport
    {
        private readonly LoopbackNetwork network;

        public LoopbackClient(LoopbackNetwork network, int id)
        {
            this.network = network;
            Id = id;
        }

        public int Id { get; }

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> MessageReceived;

        public void Connect()
        {
            Connected?.Invoke();
        }

        public void Leave()
        {
            network.ClientLeft(Id);
        }

        public void Send(byte[] data)
        {
            network.FromClient(Id, data);
        }

        internal void Deliver(byte[] data)
        {
            MessageReceived?.Invoke(data);
        }

        internal void RaiseDisconnected()
        {
            Disconnected?.Invoke();
        }
    }

    public class NetworkTests
    {
        private SpellCatalog spells;
        private LoopbackNetwork network;
        private GameServer server;

        [SetUp]
        public void SetUp()
        {
            spells = SpellCatalog.CreateDefault();
            network = new LoopbackNetwork();
            server = new GameServer(
                network,
                new ServerSettings { PlayersPerTeam = 1, PlanningSeconds = 10f, GraceSeconds = 1f },
                spells,
                new Random(1));
        }

        private (NetworkMatchSession session, LoopbackClient transport) Join(string name)
        {
            LoopbackClient transport = network.CreateClient();
            var session = new NetworkMatchSession(transport, spells, name);
            transport.Connect();
            return (session, transport);
        }

        private static TurnPlan MovePlan(NetworkMatchSession session, GridPosition target)
        {
            UnitState unit = null;
            foreach (UnitState candidate in session.State.Units)
            {
                if (candidate.PlayerId == session.LocalPlayerId)
                {
                    unit = candidate;
                }
            }

            var path = Pathfinder.FindPath(session.State.Map, unit.Position, target, 4);
            return new TurnPlan(session.LocalPlayerId, new[] { new UnitPlan(unit.Id, new UnitOrder[] { new MoveOrder(path) }) });
        }

        private static UnitState OwnUnit(NetworkMatchSession session)
        {
            foreach (UnitState unit in session.State.Units)
            {
                if (unit.PlayerId == session.LocalPlayerId)
                {
                    return unit;
                }
            }

            return null;
        }

        [Test]
        public void MatchStartsOnlyOnceEnoughPlayersAreQueued()
        {
            var first = Join("A");
            Assert.IsFalse(first.session.IsInMatch);
            Assert.AreEqual(1, server.QueuedPlayerCount);

            var second = Join("B");

            Assert.IsTrue(first.session.IsInMatch);
            Assert.IsTrue(second.session.IsInMatch);
            Assert.AreEqual(0, server.QueuedPlayerCount);
            Assert.AreEqual(1, server.ActiveMatchCount);
            Assert.AreNotEqual(first.session.LocalPlayerId, second.session.LocalPlayerId);
            Assert.AreNotEqual(OwnUnit(first.session).TeamId, OwnUnit(second.session).TeamId);
        }

        [Test]
        public void BothPlayersSubmit_RoundResolvesAndBothClientsAreSynchronized()
        {
            var a = Join("A");
            var b = Join("B");
            int resolvedA = 0;
            int resolvedB = 0;
            a.session.RoundResolved += _ => resolvedA++;
            b.session.RoundResolved += _ => resolvedB++;

            UnitState unitA = OwnUnit(a.session);
            int step = unitA.Position.X < 6 ? 2 : -2;
            GridPosition targetA = new GridPosition(unitA.Position.X + step, unitA.Position.Y);
            Assert.IsTrue(a.session.TrySubmitPlan(MovePlan(a.session, targetA), out _));
            Assert.AreEqual(0, resolvedA, "Le tour ne doit pas se résoudre tant que l'autre joueur n'a pas validé.");

            Assert.IsTrue(b.session.TrySubmitPlan(new TurnPlan(b.session.LocalPlayerId, new UnitPlan[0]), out _));

            Assert.AreEqual(1, resolvedA);
            Assert.AreEqual(1, resolvedB);
            Assert.AreEqual(targetA, OwnUnit(a.session).Position);
            Assert.AreEqual(targetA, a.session.State.GetUnit(unitA.Id).Position);
            Assert.AreEqual(targetA, b.session.State.GetUnit(unitA.Id).Position);
            Assert.AreEqual(2, a.session.State.RoundNumber);
            Assert.AreEqual(2, b.session.State.RoundNumber);
        }

        [Test]
        public void ClientCannotControlAnotherPlayersUnit()
        {
            var a = Join("A");
            var b = Join("B");
            RoundResult resultForB = null;
            b.session.RoundResolved += r => resultForB = r;

            UnitState victim = OwnUnit(b.session);
            GridPosition before = victim.Position;
            var path = Pathfinder.FindPath(a.session.State.Map, victim.Position, new GridPosition(victim.Position.X - 1, victim.Position.Y), 4);

            // A se fait passer pour le propriétaire de l'unité de B : l'identité du joueur n'est pas dans le message.
            var forged = new TurnPlan(a.session.LocalPlayerId, new[] { new UnitPlan(victim.Id, new UnitOrder[] { new MoveOrder(path) }) });
            a.session.TrySubmitPlan(forged, out _);
            b.session.TrySubmitPlan(new TurnPlan(b.session.LocalPlayerId, new UnitPlan[0]), out _);

            Assert.IsNotNull(resultForB);
            Assert.AreEqual(1, resultForB.RejectedPlans.Count);
            Assert.AreEqual(before, b.session.State.GetUnit(victim.Id).Position);
        }

        [Test]
        public void PlanningTimeout_ResolvesWithIdlePlayers()
        {
            var a = Join("A");
            var b = Join("B");
            int resolved = 0;
            a.session.RoundResolved += _ => resolved++;

            server.Update(5f);
            Assert.AreEqual(0, resolved);

            server.Update(6.5f);

            Assert.AreEqual(1, resolved);
            Assert.AreEqual(2, b.session.State.RoundNumber);
        }

        [Test]
        public void DisconnectedPlayer_IsNoLongerAwaited()
        {
            var a = Join("A");
            var b = Join("B");
            int resolved = 0;
            a.session.RoundResolved += _ => resolved++;

            b.transport.Leave();
            Assert.AreEqual(0, resolved);

            a.session.TrySubmitPlan(new TurnPlan(a.session.LocalPlayerId, new UnitPlan[0]), out _);

            Assert.AreEqual(1, resolved);
        }

        [Test]
        public void MalformedMessage_DisconnectsTheClientWithoutCrashing()
        {
            var a = Join("A");
            string failure = null;
            a.session.Failed += text => failure = text;

            a.transport.Send(new byte[] { 99, 1, 2, 3 });

            Assert.Contains(a.transport.Id, network.DisconnectedByServer);
            Assert.IsNotNull(failure);
            Assert.AreEqual(0, server.QueuedPlayerCount);
        }

        [Test]
        public void HugeElementCount_IsRejectedBeforeAllocation()
        {
            var writer = new NetWriter();
            writer.WriteByte((byte)MessageType.SubmitPlan);
            writer.WriteInt(int.MaxValue);

            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(writer.ToArray()));
        }

        [Test]
        public void TruncatedMessage_IsRejected()
        {
            byte[] full = MessageCodec.Encode(new HelloMessage(NetProtocol.Version, "Test"));
            byte[] truncated = new byte[full.Length - 2];
            Array.Copy(full, truncated, truncated.Length);

            Assert.Throws<ProtocolException>(() => MessageCodec.Decode(truncated));
        }

        [Test]
        public void WrongProtocolVersion_IsRefused()
        {
            LoopbackClient transport = network.CreateClient();
            transport.Send(MessageCodec.Encode(new HelloMessage(NetProtocol.Version + 1, "Old")));

            Assert.Contains(transport.Id, network.DisconnectedByServer);
            Assert.AreEqual(0, server.QueuedPlayerCount);
        }

        [Test]
        public void RoundResult_SurvivesEncodingRoundTrip()
        {
            var map = new GridMap(9, 9);
            var state = new MatchState(map, new MatchConfig());
            state.AddUnit(new UnitState(1, 0, 0, "A", new GridPosition(1, 1), 30, 8, 4, spells.AllIds));
            state.AddUnit(new UnitState(2, 1, 1, "B", new GridPosition(4, 1), 5, 8, 4, spells.AllIds));
            var resolver = new RoundResolver(spells, new LastTeamStandingCondition());
            RoundResult result = resolver.Resolve(state, new[]
            {
                new TurnPlan(0, new[] { new UnitPlan(1, new UnitOrder[]
                {
                    new MoveOrder(new[] { new GridPosition(2, 1) }),
                    new CastOrder("fireball", new GridPosition(4, 1))
                }) })
            });

            var message = new RoundResolvedMessage(result, 10f, MatchSnapshot.From(state));
            var decoded = (RoundResolvedMessage)MessageCodec.Decode(MessageCodec.Encode(message));

            Assert.AreEqual(result.Beats.Count, decoded.Result.Beats.Count);
            Assert.IsTrue(decoded.Result.MatchEnded);
            Assert.AreEqual(0, decoded.Result.WinnerTeamId);
            Assert.AreEqual(2, decoded.Snapshot.Units.Count);
            Assert.AreEqual(0, decoded.Snapshot.Units[1].Health);

            var damaged = (UnitDamagedEvent)decoded.Result.Beats[decoded.Result.Beats.Count - 1].Events.Find(e => e is UnitDamagedEvent);
            Assert.AreEqual(2, damaged.UnitId);
            Assert.AreEqual(6, damaged.Amount);
        }

        [Test]
        public void Snapshot_RebuildsAnIdenticalState()
        {
            MatchState original = MatchFactory.CreateTeamMatch(new MatchConfig(), spells, 3);
            original.GetUnit(2).ChangeHealth(-7);

            var decoded = (MatchStartedMessage)MessageCodec.Decode(
                MessageCodec.Encode(new MatchStartedMessage(2, 30f, MatchSnapshot.From(original))));
            MatchState rebuilt = decoded.Snapshot.CreateState();

            Assert.AreEqual(original.Units.Count, rebuilt.Units.Count);
            for (int i = 0; i < original.Units.Count; i++)
            {
                Assert.AreEqual(original.Units[i].Position, rebuilt.Units[i].Position);
                Assert.AreEqual(original.Units[i].Health, rebuilt.Units[i].Health);
                Assert.AreEqual(original.Units[i].TeamId, rebuilt.Units[i].TeamId);
            }

            Assert.AreEqual(original.Map.Width, rebuilt.Map.Width);
            Assert.AreEqual(original.Map.GetTile(new GridPosition(5, 4)), rebuilt.Map.GetTile(new GridPosition(5, 4)));
        }
    }
}
