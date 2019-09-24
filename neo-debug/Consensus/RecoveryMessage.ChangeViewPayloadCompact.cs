using Neo.IO;
using Neo.Network.P2P.Payloads;
using System.IO;

namespace Neo.Consensus
{
    partial class RecoveryMessage
    {
        public class ChangeViewPayloadCompact : ISerializable
        {
            public ushort ValidatorIndex;
            public byte OriginalViewNumber;
            public ulong Timestamp;
            public byte[] InvocationScript;

            int ISerializable.Size =>
                sizeof(ushort) +                //ValidatorIndex
                sizeof(byte) +                  //OriginalViewNumber
                sizeof(ulong) +                 //Timestamp
                InvocationScript.GetVarSize();  //InvocationScript

            void ISerializable.Deserialize(BinaryReader reader)
            {
                ValidatorIndex = reader.ReadUInt16();
                OriginalViewNumber = reader.ReadByte();
                Timestamp = reader.ReadUInt64();
                InvocationScript = reader.ReadVarBytes(1024);
            }

            public static ChangeViewPayloadCompact FromPayload(ConsensusPayload payload)
            {
                ChangeView message = payload.GetDeserializedMessage<ChangeView>();
                return new ChangeViewPayloadCompact
                {
                    ValidatorIndex = payload.ValidatorIndex,
                    OriginalViewNumber = message.ViewNumber,
                    Timestamp = message.Timestamp,
                    InvocationScript = payload.Witness.InvocationScript
                };
            }

            void ISerializable.Serialize(BinaryWriter writer)
            {
                writer.Write(ValidatorIndex);
                writer.Write(OriginalViewNumber);
                writer.Write(Timestamp);
                writer.WriteVarBytes(InvocationScript);
            }
        }
    }
}
