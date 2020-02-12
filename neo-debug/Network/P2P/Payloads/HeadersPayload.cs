using Neo.IO;
using System.IO;

namespace Neo.Network.P2P.Payloads
{
    public class HeadersPayload : ISerializable
    {
        public const int MaxHeadersCount = 2000;

        public Header[] Headers;

        public int Size => Headers.GetVarSize();

        public static HeadersPayload Create(params Header[] headers)
        {
            return new HeadersPayload
            {
                Headers = headers
            };
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            Headers = reader.ReadSerializableArray<Header>(MaxHeadersCount);
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            writer.Write(Headers);
        }
    }
}
