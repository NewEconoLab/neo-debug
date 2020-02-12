using Neo.IO;
using Neo.IO.Caching;
using System;
using System.IO;

namespace Neo.Consensus
{
    public abstract class ConsensusMessage : ISerializable
    {
        public readonly ConsensusMessageType Type;
        public byte ViewNumber;

        public virtual int Size => sizeof(ConsensusMessageType) + sizeof(byte);

        protected ConsensusMessage(ConsensusMessageType type)
        {
            if (!Enum.IsDefined(typeof(ConsensusMessageType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            this.Type = type;
        }

        public virtual void Deserialize(BinaryReader reader)
        {
            if (Type != (ConsensusMessageType)reader.ReadByte())
                throw new FormatException();
            ViewNumber = reader.ReadByte();
        }

        public static ConsensusMessage DeserializeFrom(byte[] data)
        {
            ConsensusMessageType type = (ConsensusMessageType)data[0];
            ISerializable message = ReflectionCache<ConsensusMessageType>.CreateSerializable(type, data);
            if (message is null) throw new FormatException();
            return (ConsensusMessage)message;
        }

        public virtual void Serialize(BinaryWriter writer)
        {
            writer.Write((byte)Type);
            writer.Write(ViewNumber);
        }
    }
}
