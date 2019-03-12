using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using NEL.Simple.SDK.Helper;
using Neo.Network.P2P.Payloads;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo
{
    public class ProtocolSettings
    {
        public uint Magic { get; }
        public byte AddressVersion { get; }
        public string[] StandbyValidators { get; }
        public string[] SeedList { get; }
        public IReadOnlyDictionary<TransactionType, Fixed8> SystemFee { get; }
        public Fixed8 LowPriorityThreshold { get; }
        public uint SecondsPerBlock { get; }

        public string[] MongoDbIndexs { get; private set; }
        public IReadOnlyDictionary<string, string> MongoSetting { get; private set; }

        public static ProtocolSettings Default { get; private set; }

        static ProtocolSettings()
        {
            IConfigurationSection section = new ConfigurationBuilder().AddJsonFile("protocol.json").Build().GetSection("ProtocolConfiguration");
            Default = new ProtocolSettings(section);
        }

        private ProtocolSettings(IConfigurationSection section)
        {
            this.Magic = uint.Parse(section.GetSection("Magic").Value);
            this.AddressVersion = byte.Parse(section.GetSection("AddressVersion").Value);
            this.StandbyValidators = section.GetSection("StandbyValidators").GetChildren().Select(p => p.Value).ToArray();
            this.SeedList = section.GetSection("SeedList").GetChildren().Select(p => p.Value).ToArray();
            this.SystemFee = section.GetSection("SystemFee").GetChildren().ToDictionary(p => (TransactionType)Enum.Parse(typeof(TransactionType), p.Key, true), p => Fixed8.Parse(p.Value));
            this.SecondsPerBlock = GetValueOrDefault(section.GetSection("SecondsPerBlock"), 15u, p => uint.Parse(p));
            this.LowPriorityThreshold = GetValueOrDefault(section.GetSection("LowPriorityThreshold"), Fixed8.FromDecimal(0.001m), p => Fixed8.Parse(p));
            this.MongoSetting =section.GetSection("MongoSetting").GetChildren().ToDictionary(p => p.Key, p => p.Value);
            this.MongoDbIndexs = section.GetSection("MongoDbIndexs").GetChildren().Select(p => p.Value).ToArray();

            for (var i = 0; i < this.MongoDbIndexs.Length; i++)
            {
                try
                {
                    SetMongoDbIndex(this.MongoDbIndexs[i]);
                }
                catch (Exception e)
                {
                }
            }
        }

        public void SetMongoDbIndex(string mongoDbIndex)
        {
            JObject joIndex = JObject.Parse(mongoDbIndex);
            string collName = (string)joIndex["collName"];
            JArray indexs = (JArray)joIndex["indexs"];
            for (var i = 0; i < indexs.Count; i++)
            {
                string indexName = (string)indexs[i]["indexName"];
                string indexDefinition = indexs[i]["indexDefinition"].ToString();
                bool isUnique = false;
                if (indexs[i]["isUnique"] != null)
                    isUnique = (bool)indexs[i]["isUnique"];
                MongoDBHelper.CreateIndex(this.MongoSetting["Conn"], this.MongoSetting["DataBase"], collName,indexDefinition, indexName, isUnique);
            }
        }

        internal T GetValueOrDefault<T>(IConfigurationSection section, T defaultValue, Func<string, T> selector)
        {
            if (section.Value == null) return defaultValue;
            return selector(section.Value);
        }
    }
}
