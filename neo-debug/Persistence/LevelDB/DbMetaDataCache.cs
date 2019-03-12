using NEL.Simple.SDK.Helper;
using Neo.IO;
using Neo.IO.Caching;
using Neo.IO.Data.LevelDB;
using System;

namespace Neo.Persistence.LevelDB
{
    internal class DbMetaDataCache<T> : MetaDataCache<T>
        where T : class, ICloneable<T>, ISerializable, new()
    {
        private readonly DB db;
        private readonly ReadOptions options;
        private readonly WriteBatch batch;
        private readonly byte prefix;

        public DbMetaDataCache(DB db, ReadOptions options, WriteBatch batch, byte prefix, Func<T> factory = null)
            : base(factory)
        {
            this.db = db;
            this.options = options ?? ReadOptions.Default;
            this.batch = batch;
            this.prefix = prefix;
        }

        protected override void AddInternal(T item)
        {
            batch?.Put(prefix, item.ToArray());
        }

        protected override T TryGetInternal()
        {
            if (!db.TryGet(options, prefix, out Slice slice))
                return null;
            return slice.ToArray().AsSerializable<T>();
        }

        protected override void UpdateInternal(T item)
        {
            batch?.Put(prefix, item.ToArray());
        }

        public override void Commit(ulong height)
        {
            base.Commit();
            if (string.IsNullOrEmpty(ProtocolSettings.Default.MongoSetting["Conn"]) || string.IsNullOrEmpty(ProtocolSettings.Default.MongoSetting["DataBase"]) || string.IsNullOrEmpty(ProtocolSettings.Default.MongoSetting["Task"]))
                return;
            if (State == TrackState.None)
                return;
            var wbt = new WriteBatchTask();
            wbt.tableid = prefix;
            wbt.key = null;
            wbt.value = new MongoDB.Bson.BsonBinaryData(Item.ToArray());
            wbt.state = (byte)State;
            wbt.height = height;
            MongoDBHelper.InsertOne(ProtocolSettings.Default.MongoSetting["Conn"], ProtocolSettings.Default.MongoSetting["DataBase"], ProtocolSettings.Default.MongoSetting["Task"], wbt);
        }
    }
}
