using System.Collections.Generic;
using BigProject.Core.Orders;
using BigProject.Core.Resolution;

namespace BigProject.Core.Net
{
    public enum MessageType : byte
    {
        // Client -> serveur
        Hello = 1,
        SubmitPlan = 2,
        Draft = 3,

        // Serveur -> client
        QueueStatus = 10,
        MatchStarted = 11,
        RoundResolved = 12,
        PlanRejected = 13,
        ServerError = 14,
        PlanStatus = 15,
        AllyDraft = 16
    }

    public abstract class NetMessage
    {
        public abstract MessageType Type { get; }
        internal abstract void Write(NetWriter writer);
    }

    /// <summary>
    /// Premier message d'un client : version du protocole, empreinte des sorts et pseudo. Place le joueur dans
    /// la file. Le serveur refuse un client dont les règles (version ou sorts) diffèrent des siennes.
    /// </summary>
    public sealed class HelloMessage : NetMessage
    {
        public HelloMessage(int protocolVersion, string playerName, int contentHash = 0)
        {
            ProtocolVersion = protocolVersion;
            PlayerName = playerName;
            ContentHash = contentHash;
        }

        public int ProtocolVersion { get; }
        public string PlayerName { get; }
        public int ContentHash { get; }
        public override MessageType Type => MessageType.Hello;

        internal override void Write(NetWriter writer)
        {
            writer.WriteInt(ProtocolVersion);
            writer.WriteInt(ContentHash);
            writer.WriteString(PlayerName);
        }

        internal static HelloMessage Read(NetReader reader)
        {
            int version = reader.ReadInt();
            int hash = reader.ReadInt();
            return new HelloMessage(version, reader.ReadString(), hash);
        }
    }

    /// <summary>
    /// Plan d'un joueur. Il ne contient volontairement pas d'identifiant de joueur :
    /// le serveur déduit toujours l'auteur de la connexion, jamais du contenu du message.
    /// </summary>
    public sealed class SubmitPlanMessage : NetMessage
    {
        public SubmitPlanMessage(IReadOnlyList<UnitPlan> units)
        {
            Units = units;
        }

        public IReadOnlyList<UnitPlan> Units { get; }
        public override MessageType Type => MessageType.SubmitPlan;

        internal override void Write(NetWriter writer)
        {
            writer.WriteInt(Units.Count);
            foreach (UnitPlan unitPlan in Units)
            {
                writer.WriteInt(unitPlan.UnitId);
                writer.WriteInt(unitPlan.Orders.Count);
                foreach (UnitOrder order in unitPlan.Orders)
                {
                    if (order is MoveOrder move)
                    {
                        writer.WriteByte(1);
                        writer.WriteInt(move.Path.Count);
                        foreach (var cell in move.Path)
                        {
                            writer.WritePosition(cell);
                        }
                    }
                    else if (order is CastOrder cast)
                    {
                        writer.WriteByte(2);
                        writer.WriteString(cast.SpellId);
                        writer.WritePosition(cast.Target);
                        writer.WriteInt(cast.TargetUnitId);
                    }
                    else
                    {
                        throw new ProtocolException("Type d'ordre non transportable.");
                    }
                }
            }
        }

        internal static SubmitPlanMessage Read(NetReader reader)
        {
            var units = new List<UnitPlan>();
            int unitCount = reader.ReadCount(NetLimits.MaxUnitPlans);
            for (int i = 0; i < unitCount; i++)
            {
                int unitId = reader.ReadInt();
                var orders = new List<UnitOrder>();
                int orderCount = reader.ReadCount(NetLimits.MaxOrdersPerUnit);
                for (int o = 0; o < orderCount; o++)
                {
                    byte kind = reader.ReadByte();
                    if (kind == 1)
                    {
                        int length = reader.ReadCount(NetLimits.MaxPathLength);
                        var path = new List<Grid.GridPosition>(length);
                        for (int p = 0; p < length; p++)
                        {
                            path.Add(reader.ReadPosition());
                        }

                        orders.Add(new MoveOrder(path));
                    }
                    else if (kind == 2)
                    {
                        string spellId = reader.ReadString();
                        var target = reader.ReadPosition();
                        orders.Add(new CastOrder(spellId, target, reader.ReadInt()));
                    }
                    else
                    {
                        throw new ProtocolException("Type d'ordre inconnu.");
                    }
                }

                units.Add(new UnitPlan(unitId, orders));
            }

            return new SubmitPlanMessage(units);
        }
    }

    /// <summary>
    /// Brouillon des actions d'un joueur, envoyé pendant qu'il planifie. Le serveur ne le relaie qu'aux
    /// alliés (jamais aux ennemis) ; ce n'est pas un plan validé.
    /// </summary>
    public sealed class DraftMessage : NetMessage
    {
        public DraftMessage(IReadOnlyList<UnitPlan> units)
        {
            Units = units;
        }

        public IReadOnlyList<UnitPlan> Units { get; }
        public override MessageType Type => MessageType.Draft;

        internal override void Write(NetWriter writer)
        {
            new SubmitPlanMessage(Units).Write(writer);
        }

        internal static DraftMessage Read(NetReader reader)
        {
            return new DraftMessage(SubmitPlanMessage.Read(reader).Units);
        }
    }

    /// <summary>Brouillon d'un allié, relayé par le serveur à son équipe uniquement.</summary>
    public sealed class AllyDraftMessage : NetMessage
    {
        public AllyDraftMessage(IReadOnlyList<UnitPlan> units)
        {
            Units = units;
        }

        public IReadOnlyList<UnitPlan> Units { get; }
        public override MessageType Type => MessageType.AllyDraft;

        internal override void Write(NetWriter writer)
        {
            new SubmitPlanMessage(Units).Write(writer);
        }

        internal static AllyDraftMessage Read(NetReader reader)
        {
            return new AllyDraftMessage(SubmitPlanMessage.Read(reader).Units);
        }
    }

    public sealed class QueueStatusMessage : NetMessage
    {
        public QueueStatusMessage(int waiting, int needed)
        {
            Waiting = waiting;
            Needed = needed;
        }

        public int Waiting { get; }
        public int Needed { get; }
        public override MessageType Type => MessageType.QueueStatus;

        internal override void Write(NetWriter writer)
        {
            writer.WriteInt(Waiting);
            writer.WriteInt(Needed);
        }

        internal static QueueStatusMessage Read(NetReader reader)
        {
            return new QueueStatusMessage(reader.ReadInt(), reader.ReadInt());
        }
    }

    public sealed class MatchStartedMessage : NetMessage
    {
        public MatchStartedMessage(int localPlayerId, float planningSeconds, MatchSnapshot snapshot)
        {
            LocalPlayerId = localPlayerId;
            PlanningSeconds = planningSeconds;
            Snapshot = snapshot;
        }

        public int LocalPlayerId { get; }
        /// <summary>Durée de la phase de planification, 0 = illimitée.</summary>
        public float PlanningSeconds { get; }
        public MatchSnapshot Snapshot { get; }
        public override MessageType Type => MessageType.MatchStarted;

        internal override void Write(NetWriter writer)
        {
            writer.WriteInt(LocalPlayerId);
            writer.WriteFloat(PlanningSeconds);
            Snapshot.Write(writer);
        }

        internal static MatchStartedMessage Read(NetReader reader)
        {
            int playerId = reader.ReadInt();
            float seconds = reader.ReadFloat();
            return new MatchStartedMessage(playerId, seconds, MatchSnapshot.Read(reader));
        }
    }

    public sealed class RoundResolvedMessage : NetMessage
    {
        public RoundResolvedMessage(RoundResult result, float planningSeconds, MatchSnapshot snapshot)
        {
            Result = result;
            PlanningSeconds = planningSeconds;
            Snapshot = snapshot;
        }

        public RoundResult Result { get; }
        public float PlanningSeconds { get; }
        public MatchSnapshot Snapshot { get; }
        public override MessageType Type => MessageType.RoundResolved;

        internal override void Write(NetWriter writer)
        {
            RoundResultCodec.Write(writer, Result);
            writer.WriteFloat(PlanningSeconds);
            Snapshot.Write(writer);
        }

        internal static RoundResolvedMessage Read(NetReader reader)
        {
            RoundResult result = RoundResultCodec.Read(reader);
            float seconds = reader.ReadFloat();
            return new RoundResolvedMessage(result, seconds, MatchSnapshot.Read(reader));
        }
    }

    public sealed class PlanRejectedMessage : NetMessage
    {
        public PlanRejectedMessage(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
        public override MessageType Type => MessageType.PlanRejected;

        internal override void Write(NetWriter writer)
        {
            writer.WriteReason(Reason);
        }

        internal static PlanRejectedMessage Read(NetReader reader)
        {
            return new PlanRejectedMessage(reader.ReadReason());
        }
    }

    /// <summary>Progression de la planification : combien de joueurs ont validé leur tour.</summary>
    public sealed class PlanStatusMessage : NetMessage
    {
        public PlanStatusMessage(int ready, int expected)
        {
            Ready = ready;
            Expected = expected;
        }

        public int Ready { get; }
        public int Expected { get; }
        public override MessageType Type => MessageType.PlanStatus;

        internal override void Write(NetWriter writer)
        {
            writer.WriteInt(Ready);
            writer.WriteInt(Expected);
        }

        internal static PlanStatusMessage Read(NetReader reader)
        {
            return new PlanStatusMessage(reader.ReadInt(), reader.ReadInt());
        }
    }

    public sealed class ServerErrorMessage : NetMessage
    {
        public ServerErrorMessage(string text)
        {
            Text = text;
        }

        public string Text { get; }
        public override MessageType Type => MessageType.ServerError;

        internal override void Write(NetWriter writer)
        {
            writer.WriteReason(Text);
        }

        internal static ServerErrorMessage Read(NetReader reader)
        {
            return new ServerErrorMessage(reader.ReadReason());
        }
    }
}
