using System.Collections.Generic;
using BigProject.Core.Match;
using BigProject.Core.Resolution;

namespace BigProject.Core.Net
{
    /// <summary>Encodage du résultat d'un tour : les événements que les clients rejouent.</summary>
    internal static class RoundResultCodec
    {
        private const byte UnitMoved = 1;
        private const byte MoveBlocked = 2;
        private const byte SpellCast = 3;
        private const byte SpellFizzled = 4;
        private const byte UnitDamaged = 5;
        private const byte UnitHealed = 6;
        private const byte UnitDied = 7;

        public static void Write(NetWriter writer, RoundResult result)
        {
            writer.WriteInt(result.RoundNumber);
            writer.WriteBool(result.MatchEnded);
            writer.WriteInt(result.WinnerTeamId);

            writer.WriteInt(result.RejectedPlans.Count);
            foreach (RejectedPlan rejected in result.RejectedPlans)
            {
                writer.WriteInt(rejected.UnitId);
                writer.WriteReason(rejected.Reason);
            }

            writer.WriteInt(result.Beats.Count);
            foreach (ResolutionBeat beat in result.Beats)
            {
                writer.WriteInt(beat.StepIndex);
                writer.WriteInt(beat.Events.Count);
                foreach (GameEvent gameEvent in beat.Events)
                {
                    WriteEvent(writer, gameEvent);
                }
            }
        }

        public static RoundResult Read(NetReader reader)
        {
            var result = new RoundResult(reader.ReadInt())
            {
                MatchEnded = reader.ReadBool(),
                WinnerTeamId = reader.ReadInt()
            };

            int rejectedCount = reader.ReadCount(NetLimits.MaxRejectedPlans);
            for (int i = 0; i < rejectedCount; i++)
            {
                result.RejectedPlans.Add(new RejectedPlan(reader.ReadInt(), reader.ReadReason()));
            }

            int beatCount = reader.ReadCount(NetLimits.MaxBeats);
            for (int b = 0; b < beatCount; b++)
            {
                var beat = new ResolutionBeat { StepIndex = reader.ReadCount(NetLimits.MaxOrdersPerUnit) };
                int eventCount = reader.ReadCount(NetLimits.MaxEventsPerBeat);
                for (int e = 0; e < eventCount; e++)
                {
                    beat.Events.Add(ReadEvent(reader));
                }

                result.Beats.Add(beat);
            }

            return result;
        }

        private static void WriteEvent(NetWriter writer, GameEvent gameEvent)
        {
            switch (gameEvent)
            {
                case UnitMovedEvent moved:
                    writer.WriteByte(UnitMoved);
                    writer.WriteInt(moved.UnitId);
                    writer.WritePosition(moved.From);
                    writer.WritePosition(moved.To);
                    break;

                case MoveBlockedEvent blocked:
                    writer.WriteByte(MoveBlocked);
                    writer.WriteInt(blocked.UnitId);
                    writer.WritePosition(blocked.At);
                    writer.WritePosition(blocked.Attempted);
                    break;

                case SpellCastEvent cast:
                    writer.WriteByte(SpellCast);
                    writer.WriteInt(cast.CasterId);
                    writer.WriteString(cast.SpellId);
                    writer.WritePosition(cast.Target);
                    writer.WriteInt(cast.AffectedCells.Count);
                    foreach (var cell in cast.AffectedCells)
                    {
                        writer.WritePosition(cell);
                    }

                    break;

                case SpellFizzledEvent fizzled:
                    writer.WriteByte(SpellFizzled);
                    writer.WriteInt(fizzled.CasterId);
                    writer.WriteString(fizzled.SpellId);
                    writer.WritePosition(fizzled.Target);
                    writer.WriteReason(fizzled.Reason);
                    break;

                case UnitDamagedEvent damaged:
                    writer.WriteByte(UnitDamaged);
                    writer.WriteInt(damaged.UnitId);
                    writer.WriteInt(damaged.Amount);
                    writer.WriteInt(damaged.HealthAfter);
                    break;

                case UnitHealedEvent healed:
                    writer.WriteByte(UnitHealed);
                    writer.WriteInt(healed.UnitId);
                    writer.WriteInt(healed.Amount);
                    writer.WriteInt(healed.HealthAfter);
                    break;

                case UnitDiedEvent died:
                    writer.WriteByte(UnitDied);
                    writer.WriteInt(died.UnitId);
                    break;

                default:
                    throw new ProtocolException("Événement non transportable : " + gameEvent.GetType().Name);
            }
        }

        private static GameEvent ReadEvent(NetReader reader)
        {
            switch (reader.ReadByte())
            {
                case UnitMoved:
                    return new UnitMovedEvent(reader.ReadInt(), reader.ReadPosition(), reader.ReadPosition());

                case MoveBlocked:
                    return new MoveBlockedEvent(reader.ReadInt(), reader.ReadPosition(), reader.ReadPosition());

                case SpellCast:
                {
                    int caster = reader.ReadInt();
                    string spellId = reader.ReadString();
                    var target = reader.ReadPosition();
                    int count = reader.ReadCount(NetLimits.MaxCells);
                    var cells = new List<Grid.GridPosition>(count);
                    for (int i = 0; i < count; i++)
                    {
                        cells.Add(reader.ReadPosition());
                    }

                    return new SpellCastEvent(caster, spellId, target, cells);
                }

                case SpellFizzled:
                    return new SpellFizzledEvent(reader.ReadInt(), reader.ReadString(), reader.ReadPosition(), reader.ReadReason());

                case UnitDamaged:
                    return new UnitDamagedEvent(reader.ReadInt(), reader.ReadInt(), reader.ReadInt());

                case UnitHealed:
                    return new UnitHealedEvent(reader.ReadInt(), reader.ReadInt(), reader.ReadInt());

                case UnitDied:
                    return new UnitDiedEvent(reader.ReadInt());

                default:
                    throw new ProtocolException("Événement inconnu.");
                    }
        }
    }

    /// <summary>Point d'entrée unique pour transformer un message en octets et inversement.</summary>
    public static class MessageCodec
    {
        public static byte[] Encode(NetMessage message)
        {
            var writer = new NetWriter();
            writer.WriteByte((byte)message.Type);
            message.Write(writer);
            return writer.ToArray();
        }

        /// <summary>Décode des octets non fiables. Lève ProtocolException pour tout message invalide.</summary>
        public static NetMessage Decode(byte[] data)
        {
            var reader = new NetReader(data);
            try
            {
                NetMessage message = DecodeBody(reader, reader.ReadEnum<MessageType>());
                reader.EnsureFullyConsumed();
                return message;
            }
            catch (System.IO.EndOfStreamException)
            {
                throw new ProtocolException("Message tronqué.");
            }
        }

        private static NetMessage DecodeBody(NetReader reader, MessageType type)
        {
            switch (type)
            {
                case MessageType.Hello: return HelloMessage.Read(reader);
                case MessageType.SubmitPlan: return SubmitPlanMessage.Read(reader);
                case MessageType.Draft: return DraftMessage.Read(reader);
                case MessageType.AllyDraft: return AllyDraftMessage.Read(reader);
                case MessageType.QueueStatus: return QueueStatusMessage.Read(reader);
                case MessageType.MatchStarted: return MatchStartedMessage.Read(reader);
                case MessageType.RoundResolved: return RoundResolvedMessage.Read(reader);
                case MessageType.PlanRejected: return PlanRejectedMessage.Read(reader);
                case MessageType.ServerError: return ServerErrorMessage.Read(reader);
                case MessageType.PlanStatus: return PlanStatusMessage.Read(reader);
                default: throw new ProtocolException("Type de message inconnu.");
            }
        }
    }
}
