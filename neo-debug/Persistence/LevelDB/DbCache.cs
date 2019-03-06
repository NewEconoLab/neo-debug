﻿using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using System;
using System.Collections.Generic;
using NEL.Simple.SDK.Helper;

namespace Neo.Persistence.LevelDB
{
    internal class DbCache<TKey, TValue> : DataCache<TKey, TValue>
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

        public override void Commit(ulong height)
        {
            base.Commit();
            if (string.IsNullOrEmpty(Settings.Default.MongoSetting["Conn"]) || string.IsNullOrEmpty(Settings.Default.MongoSetting["DataBase"]) || string.IsNullOrEmpty(Settings.Default.MongoSetting["Task"]))
                return;
            WriteBatchTask wbt;
            foreach (var i in this.dictionary.Values)
            {
                if (i.State == TrackState.None)
                    continue;
                wbt = new WriteBatchTask();
                wbt.tableid = prefix;
                wbt.key = new MongoDB.Bson.BsonBinaryData(i.Key.ToArray());
                wbt.value = new MongoDB.Bson.BsonBinaryData(i.Item.ToArray());
                wbt.valuehash = new MongoDB.Bson.BsonBinaryData(Cryptography.Crypto.Default.Hash256(i.Item.ToArray()));
                wbt.state = (byte)i.State;
                wbt.height = height;
                MongoDBHelper.InsertOne(Settings.Default.MongoSetting["Conn"], Settings.Default.MongoSetting["DataBase"], Settings.Default.MongoSetting["Task"], wbt);
            }
        }
    }

    class WriteBatchTask
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
