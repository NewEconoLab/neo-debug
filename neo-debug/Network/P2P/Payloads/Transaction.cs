using Neo.Cryptography;
using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Neo.Wallets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Array = Neo.VM.Types.Array;

namespace Neo.Network.P2P.Payloads
{
    public class Transaction : IEquatable<Transaction>, IInventory, IInteroperable
    {
        public const int MaxTransactionSize = 102400;
        public const uint MaxValidUntilBlockIncrement = 2102400;
        /// <summary>
        /// Maximum number of attributes that can be contained within a transaction
        /// </summary>
        public const int MaxTransactionAttributes = 16;

        private byte version;
        private uint nonce;
        private long sysfee;
        private long netfee;
        private uint validUntilBlock;
        private Signer[] _signers;
        private TransactionAttribute[] attributes;
        private byte[] script;
        private Witness[] witnesses;

        public const int HeaderSize =
            sizeof(byte) +  //Version
            sizeof(uint) +  //Nonce
            sizeof(long) +  //SystemFee
            sizeof(long) +  //NetworkFee
            sizeof(uint);   //ValidUntilBlock

        private Dictionary<Type, TransactionAttribute[]> _attributesCache;
        public TransactionAttribute[] Attributes
        {
            get => attributes;
            set { attributes = value; _attributesCache = null; _hash = null; _size = 0; }
        }

        /// <summary>
        /// The <c>NetworkFee</c> for the transaction divided by its <c>Size</c>.
        /// <para>Note that this property must be used with care. Getting the value of this property multiple times will return the same result. The value of this property can only be obtained after the transaction has been completely built (no longer modified).</para>
        /// </summary>
        public long FeePerByte => NetworkFee / Size;

        private UInt256 _hash = null;
        public UInt256 Hash
        {
            get
            {
                if (_hash == null)
                {
                    _hash = new UInt256(Crypto.Hash256(this.GetHashData()));
                }
                return _hash;
            }
        }

        InventoryType IInventory.InventoryType => InventoryType.TX;

        /// <summary>
        /// Distributed to consensus nodes.
        /// </summary>
        public long NetworkFee
        {
            get => netfee;
            set { netfee = value; _hash = null; }
        }

        public uint Nonce
        {
            get => nonce;
            set { nonce = value; _hash = null; }
        }

        public byte[] Script
        {
            get => script;
            set { script = value; _hash = null; _size = 0; }
        }

        /// <summary>
        /// The first signer is the sender of the transaction, regardless of its WitnessScope.
        /// The sender will pay the fees of the transaction.
        /// </summary>
        public UInt160 Sender => _signers[0].Account;

        public Signer[] Signers
        {
            get => _signers;
            set { _signers = value; _hash = null; _size = 0; }
        }

        private int _size;
        public int Size
        {
            get
            {
                if (_size == 0)
                {
                    _size = HeaderSize +
                        Signers.GetVarSize() +      // Signers
                        Attributes.GetVarSize() +   // Attributes
                        Script.GetVarSize() +       // Script
                        Witnesses.GetVarSize();     // Witnesses
                }
                return _size;
            }
        }

        /// <summary>
        /// Fee to be burned.
        /// </summary>
        public long SystemFee
        {
            get => sysfee;
            set { sysfee = value; _hash = null; }
        }

        public uint ValidUntilBlock
        {
            get => validUntilBlock;
            set { validUntilBlock = value; _hash = null; }
        }

        public byte Version
        {
            get => version;
            set { version = value; _hash = null; }
        }

        public Witness[] Witnesses
        {
            get => witnesses;
            set { witnesses = value; _size = 0; }
        }

        void ISerializable.Deserialize(BinaryReader reader)
        {
            int startPosition = -1;
            if (reader.BaseStream.CanSeek)
                startPosition = (int)reader.BaseStream.Position;
            DeserializeUnsigned(reader);
            Witnesses = reader.ReadSerializableArray<Witness>();
            if (startPosition >= 0)
                _size = (int)reader.BaseStream.Position - startPosition;
        }

        private static IEnumerable<TransactionAttribute> DeserializeAttributes(BinaryReader reader, int maxCount)
        {
            int count = (int)reader.ReadVarInt((ulong)maxCount);
            HashSet<TransactionAttributeType> hashset = new HashSet<TransactionAttributeType>();
            while (count-- > 0)
            {
                TransactionAttribute attribute = TransactionAttribute.DeserializeFrom(reader);
                if (!attribute.AllowMultiple && !hashset.Add(attribute.Type))
                    throw new FormatException();
                yield return attribute;
            }
        }

        private static IEnumerable<Signer> DeserializeSigners(BinaryReader reader, int maxCount)
        {
            int count = (int)reader.ReadVarInt((ulong)maxCount);
            if (count == 0) throw new FormatException();
            HashSet<UInt160> hashset = new HashSet<UInt160>();
            for (int i = 0; i < count; i++)
            {
                Signer signer = reader.ReadSerializable<Signer>();
                if (!hashset.Add(signer.Account)) throw new FormatException();
                yield return signer;
            }
        }

        public void DeserializeUnsigned(BinaryReader reader)
        {
            Version = reader.ReadByte();
            if (Version > 0) throw new FormatException();
            Nonce = reader.ReadUInt32();
            SystemFee = reader.ReadInt64();
            if (SystemFee < 0) throw new FormatException();
            NetworkFee = reader.ReadInt64();
            if (NetworkFee < 0) throw new FormatException();
            if (SystemFee + NetworkFee < SystemFee) throw new FormatException();
            ValidUntilBlock = reader.ReadUInt32();
            Signers = DeserializeSigners(reader, MaxTransactionAttributes).ToArray();
            Attributes = DeserializeAttributes(reader, MaxTransactionAttributes - Signers.Length).ToArray();
            Script = reader.ReadVarBytes(ushort.MaxValue);
            if (Script.Length == 0) throw new FormatException();
        }

        public bool Equals(Transaction other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Hash.Equals(other.Hash);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Transaction);
        }

        void IInteroperable.FromStackItem(StackItem stackItem)
        {
            throw new NotSupportedException();
        }

        public T GetAttribute<T>() where T : TransactionAttribute
        {
            return GetAttributes<T>()?.First();
        }

        public T[] GetAttributes<T>() where T : TransactionAttribute
        {
            _attributesCache ??= attributes.GroupBy(p => p.GetType()).ToDictionary(p => p.Key, p => (TransactionAttribute[])p.OfType<T>().ToArray());
            _attributesCache.TryGetValue(typeof(T), out var result);
            return (T[])result;
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }

        public UInt160[] GetScriptHashesForVerifying(StoreView snapshot)
        {
            return Signers.Select(p => p.Account).ToArray();
        }

        void ISerializable.Serialize(BinaryWriter writer)
        {
            ((IVerifiable)this).SerializeUnsigned(writer);
            writer.Write(Witnesses);
        }

        void IVerifiable.SerializeUnsigned(BinaryWriter writer)
        {
            writer.Write(Version);
            writer.Write(Nonce);
            writer.Write(SystemFee);
            writer.Write(NetworkFee);
            writer.Write(ValidUntilBlock);
            writer.Write(Signers);
            writer.Write(Attributes);
            writer.WriteVarBytes(Script);
        }

        public JObject ToJson()
        {
            JObject json = new JObject();
            json["hash"] = Hash.ToString();
            json["size"] = Size;
            json["version"] = Version;
            json["nonce"] = Nonce;
            json["sender"] = Sender.ToAddress();
            json["sysfee"] = SystemFee.ToString();
            json["netfee"] = NetworkFee.ToString();
            json["validuntilblock"] = ValidUntilBlock;
            json["signers"] = Signers.Select(p => p.ToJson()).ToArray();
            json["attributes"] = Attributes.Select(p => p.ToJson()).ToArray();
            json["script"] = Convert.ToBase64String(Script);
            json["witnesses"] = Witnesses.Select(p => p.ToJson()).ToArray();
            return json;
        }

        bool IInventory.Verify(StoreView snapshot)
        {
            return Verify(snapshot, null) == VerifyResult.Succeed;
        }

        public virtual VerifyResult VerifyForEachBlock(StoreView snapshot, TransactionVerificationContext context)
        {
            if (ValidUntilBlock <= snapshot.Height || ValidUntilBlock > snapshot.Height + MaxValidUntilBlockIncrement)
                return VerifyResult.Expired;
            UInt160[] hashes = GetScriptHashesForVerifying(snapshot);
            if (NativeContract.Policy.GetBlockedAccounts(snapshot).Intersect(hashes).Any())
                return VerifyResult.PolicyFail;
            if (NativeContract.Policy.GetMaxBlockSystemFee(snapshot) < SystemFee)
                return VerifyResult.PolicyFail;
            if (!(context?.CheckTransaction(this, snapshot) ?? true)) return VerifyResult.InsufficientFunds;
            foreach (TransactionAttribute attribute in Attributes)
                if (!attribute.Verify(snapshot, this))
                    return VerifyResult.Invalid;
            if (hashes.Length != Witnesses.Length) return VerifyResult.Invalid;
            for (int i = 0; i < hashes.Length; i++)
            {
                if (Witnesses[i].VerificationScript.Length > 0) continue;
                if (snapshot.Contracts.TryGet(hashes[i]) is null) return VerifyResult.Invalid;
            }
            return VerifyResult.Succeed;
        }

        public virtual VerifyResult Verify(StoreView snapshot, TransactionVerificationContext context)
        {
            VerifyResult result = VerifyForEachBlock(snapshot, context);
            if (result != VerifyResult.Succeed) return result;
            if (Size > MaxTransactionSize) return VerifyResult.Invalid;
            long net_fee = NetworkFee - Size * NativeContract.Policy.GetFeePerByte(snapshot);
            if (net_fee < 0) return VerifyResult.InsufficientFunds;
            if (!this.VerifyWitnesses(snapshot, net_fee)) return VerifyResult.Invalid;
            return VerifyResult.Succeed;
        }

        public StackItem ToStackItem(ReferenceCounter referenceCounter)
        {
            return new Array(referenceCounter, new StackItem[]
            {
                // Computed properties
                Hash.ToArray(),

                // Transaction properties
                (int)Version,
                Nonce,
                Sender.ToArray(),
                SystemFee,
                NetworkFee,
                ValidUntilBlock,
                Script,
            });
        }
    }
}
