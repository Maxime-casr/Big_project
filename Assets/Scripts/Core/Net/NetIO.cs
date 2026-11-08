using System;
using System.IO;
using System.Text;
using BigProject.Core.Grid;

namespace BigProject.Core.Net
{
    public static class NetProtocol
    {
        /// <summary>À incrémenter à chaque changement de protocole ou de règles partagées (catalogue de sorts compris).</summary>
        public const int Version = 1;
    }

    /// <summary>Bornes appliquées à tout ce qui est lu sur le réseau : un client malveillant ne peut pas forcer d'allocation géante.</summary>
    public static class NetLimits
    {
        public const int MaxMessageBytes = 64 * 1024;
        public const int MaxStringBytes = 64;
        public const int MaxReasonBytes = 256;
        public const int MaxReasonChars = 80;
        public const int MaxMapSide = 64;
        public const int MaxUnits = 64;
        public const int MaxSpellsPerUnit = 16;
        public const int MaxUnitPlans = 16;
        public const int MaxOrdersPerUnit = 32;
        public const int MaxPathLength = 64;
        public const int MaxBeats = 512;
        public const int MaxEventsPerBeat = 128;
        public const int MaxCells = 256;
        public const int MaxRejectedPlans = 64;
    }

    /// <summary>Message réseau invalide ou hors limites.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message)
        {
        }
    }

    public sealed class NetWriter
    {
        private readonly MemoryStream stream = new MemoryStream();
        private readonly BinaryWriter writer;

        public NetWriter()
        {
            writer = new BinaryWriter(stream, Encoding.UTF8);
        }

        public void WriteByte(byte value)
        {
            writer.Write(value);
        }

        public void WriteBool(bool value)
        {
            writer.Write(value);
        }

        public void WriteInt(int value)
        {
            writer.Write(value);
        }

        public void WriteFloat(float value)
        {
            writer.Write(value);
        }

        public void WriteString(string value)
        {
            WriteBounded(value, NetLimits.MaxStringBytes);
        }

        /// <summary>Message d'erreur ou de raison : plus long qu'un identifiant, tronqué si besoin.</summary>
        public void WriteReason(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > NetLimits.MaxReasonChars)
            {
                text = text.Substring(0, NetLimits.MaxReasonChars);
            }

            WriteBounded(text, NetLimits.MaxReasonBytes);
        }

        private void WriteBounded(string value, int maxBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > maxBytes)
            {
                throw new ProtocolException("Chaîne trop longue.");
            }

            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        public void WritePosition(GridPosition position)
        {
            writer.Write(position.X);
            writer.Write(position.Y);
        }

        public byte[] ToArray()
        {
            writer.Flush();
            return stream.ToArray();
        }
    }

    public sealed class NetReader
    {
        private readonly MemoryStream stream;
        private readonly BinaryReader reader;

        public NetReader(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > NetLimits.MaxMessageBytes)
            {
                throw new ProtocolException("Taille de message invalide.");
            }

            stream = new MemoryStream(data, false);
            reader = new BinaryReader(stream, Encoding.UTF8);
        }

        public byte ReadByte()
        {
            return reader.ReadByte();
        }

        public bool ReadBool()
        {
            return reader.ReadBoolean();
        }

        public int ReadInt()
        {
            return reader.ReadInt32();
        }

        public float ReadFloat()
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ProtocolException("Nombre invalide.");
            }

            return value;
        }

        public string ReadString()
        {
            return ReadBounded(NetLimits.MaxStringBytes);
        }

        public string ReadReason()
        {
            return ReadBounded(NetLimits.MaxReasonBytes);
        }

        private string ReadBounded(int maxBytes)
        {
            int length = reader.ReadUInt16();
            if (length > maxBytes)
            {
                throw new ProtocolException("Chaîne trop longue.");
            }

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new ProtocolException("Message tronqué.");
            }

            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>Lit un nombre d'éléments et refuse toute valeur négative ou supérieure à la borne.</summary>
        public int ReadCount(int max)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > max)
            {
                throw new ProtocolException("Nombre d'éléments hors limites.");
            }

            return count;
        }

        public GridPosition ReadPosition()
        {
            int x = reader.ReadInt32();
            int y = reader.ReadInt32();
            return new GridPosition(x, y);
        }

        public T ReadEnum<T>() where T : struct, Enum
        {
            byte raw = reader.ReadByte();
            var value = (T)Enum.ToObject(typeof(T), raw);
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new ProtocolException("Valeur d'énumération inconnue.");
            }

            return value;
        }

        public void EnsureFullyConsumed()
        {
            if (stream.Position != stream.Length)
            {
                throw new ProtocolException("Données en trop à la fin du message.");
            }
        }
    }
}
