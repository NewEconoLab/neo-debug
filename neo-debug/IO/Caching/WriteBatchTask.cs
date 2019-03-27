using System;

namespace Neo.IO.Caching
{
    public enum EnumDataTpye
    {
        native,
        application,
        nep5
    }

    public class WriteBatchTask
    {
        public WriteBatchOperation writeBatchOperation;
        public EnumDataTpye enumDataTpye;
    }

    public class WriteBatchOperation
    {
        public MongoDB.Bson.ObjectId _id { get; private set; }
        public byte tableid;
        public MongoDB.Bson.BsonBinaryData key;
        public MongoDB.Bson.BsonBinaryData value;
        public MongoDB.Bson.BsonBinaryData valuehash;
        public byte state;
        public UInt64 height;
    }
}
