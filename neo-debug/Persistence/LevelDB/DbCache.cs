using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using System;
using System.Collections.Generic;
using NEL.Simple.SDK.Helper;

namespace Neo.Persistence.LevelDB
{
    public class DbCache<TKey, TValue> : DataCache<TKey, TValue>
        where TKey : IEquatable<TKey>, ISerializable, new()
        where TValue : class, ICloneable<TValue>, ISerializable, new()
    {
        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteBatch batch;
        private readonly byte prefix;

        public DbCache(DB db, ReadOptions options, WriteBatch batch, byte prefix)
        {
            this.db = db;
            this.options = options ?? ReadOptions.Default;
            this.batch = batch;
            this.prefix = prefix;
        }

        protected override void AddInternal(TKey key, TValue value)
        {
            batch?.Put(prefix, key, value);
        }

        public override void DeleteInternal(TKey key)
        {
            batch?.Delete(prefix, key);
        }

        protected override IEnumerable<KeyValuePair<TKey, TValue>> FindInternal(byte[] key_prefix)
        {
            return db.Find(options, SliceBuilder.Begin(prefix).Add(key_prefix), (k, v) => new KeyValuePair<TKey, TValue>(k.ToArray().AsSerializable<TKey>(1), v.ToArray().AsSerializable<TValue>()));
        }

        protected override TValue GetInternal(TKey key)
        {
            return db.Get<TValue>(options, prefix, key);
        }

        protected override TValue TryGetInternal(TKey key)
        {
            return db.TryGet<TValue>(options, prefix, key);
        }

        protected override void UpdateInternal(TKey key, TValue value)
        {
            batch?.Put(prefix, key, value);
        }

        public override void Commit(ulong height,EnumDataTpye enumDataTpye = EnumDataTpye.native)
        {
            base.Commit();

            foreach (var i in this.dictionary.Values)
            {
                if (i.State == TrackState.None)
                    continue;
                Plugins.Plugin.RecordToMongo(new WriteBatchTask
                {
                    enumDataTpye = enumDataTpye,
                    writeBatchOperation = new WriteBatchOperation()
                    {
                        tableid = prefix,
                        key = new MongoDB.Bson.BsonBinaryData(i.Key.ToArray()),
                        value = new MongoDB.Bson.BsonBinaryData(i.Item.ToArray()),
                        valuehash = new MongoDB.Bson.BsonBinaryData(Cryptography.Crypto.Default.Hash256(i.Item.ToArray())),
                        state = (byte)i.State,
                        height = height
                    }
                });
            }
        }
    }
}
