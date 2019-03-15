using System;
using System.Collections.Generic;
using System.Text;

namespace Neo.Persistence
{
    public class WriteBatchTask
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
