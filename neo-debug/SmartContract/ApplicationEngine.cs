using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Debug;
using Neo.VM;
using Neo.VM.Types;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Neo.SmartContract
{
    public class ApplicationEngine : ExecutionEngine
    {
        private const long ratio = 100000;
        private const long gas_free = 10 * 100000000;
        private readonly long gas_amount;
        private long gas_consumed = 0;
        private readonly bool testMode;
        private readonly Snapshot snapshot;

        public Fixed8 GasConsumed => new Fixed8(gas_consumed);
        public new NeoService Service => (NeoService)base.Service;

        public ApplicationEngine(TriggerType trigger, IScriptContainer container, Snapshot snapshot, Fixed8 gas, bool testMode = false)
            : base(container, Cryptography.Crypto.Default, snapshot, new NeoService(trigger, snapshot))
        {
            this.gas_amount = gas_free + gas.GetData();
            this.testMode = testMode;
            this.snapshot = snapshot;
        }

        private bool CheckDynamicInvoke()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            switch (instruction.OpCode)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    if (instruction.Operand.NotZero()) return true;
                    // if we get this far it is a dynamic call
                    // now look at the current executing script
                    // to determine if it can do dynamic calls
                    return snapshot.Contracts[new UInt160(CurrentContext.ScriptHash)].HasDynamicInvoke;
                case OpCode.CALL_ED:
                case OpCode.CALL_EDT:
                    return snapshot.Contracts[new UInt160(CurrentContext.ScriptHash)].HasDynamicInvoke;
                default:
                    return true;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            Service.Dispose();
        }

        public new bool Execute()
        {
            try
            {
                while (true)
                {
                    OpCode curOpcode = CurrentContext.CurrentInstruction.OpCode;
                    if (this.DumpInfo != null)
                    {
                        this.DumpInfo.NextOp(CurrentContext.InstructionPointer, curOpcode);
                        this.CurrentContext.EvaluationStack.ClearRecord();
                    }
                    ExecuteNext();
                    if (State.HasFlag(VMState.HALT) || State.HasFlag(VMState.FAULT))
                        break;
                    if (DumpInfo != null)
                    {
                        if (State.HasFlag(VMState.HALT) || State.HasFlag(VMState.FAULT))
                            continue;
                        var EvaluationStackRec = this.CurrentContext.EvaluationStack;
                        VM.StackItem result = null;
                        ExecutionStackRecord.Op[] record = EvaluationStackRec.record.ToArray();
                        var ltype = EvaluationStackRec.GetLastRecordType();
                        if (ltype == ExecutionStackRecord.OpType.Push)
                        {
                            result = EvaluationStackRec.PeekWithoutLog();
                        }
                        else if (ltype == ExecutionStackRecord.OpType.Insert)
                        {
                            result = EvaluationStackRec.PeekWithoutLog(EvaluationStackRec.record.Last().ind);
                        }
                        else if (ltype == ExecutionStackRecord.OpType.Set)
                        {
                            result = EvaluationStackRec.PeekWithoutLog(EvaluationStackRec.record.Last().ind);
                        }
                        else if (ltype == ExecutionStackRecord.OpType.Peek)
                        {
                            result = EvaluationStackRec.PeekWithoutLog();
                        }
                        LogResult(curOpcode, record, result);
                    }
                }
            }
            catch
            {
                State |= VMState.FAULT;
                return false;
            }
            if (DumpInfo != null)
            {
                DumpInfo.Finish(State);
            }
            return !State.HasFlag(VMState.FAULT);
        }
        protected virtual long GetPrice()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            if (instruction.OpCode <= OpCode.NOP) return 0;
            switch (instruction.OpCode)
            {
                case OpCode.APPCALL:
                case OpCode.TAILCALL:
                    return 10;
                case OpCode.SYSCALL:
                    return GetPriceForSysCall();
                case OpCode.SHA1:
                case OpCode.SHA256:
                    return 10;
                case OpCode.HASH160:
                case OpCode.HASH256:
                    return 20;
                case OpCode.CHECKSIG:
                case OpCode.VERIFY:
                    return 100;
                case OpCode.CHECKMULTISIG:
                    {
                        if (CurrentContext.EvaluationStack.Count == 0) return 1;

                        var item = CurrentContext.EvaluationStack.Peek();

                        int n;
                        if (item is Array array) n = array.Count;
                        else n = (int)item.GetBigInteger();

                        if (n < 1) return 1;
                        return 100 * n;
                    }
                default: return 1;
            }
        }

        protected virtual long GetPriceForSysCall()
        {
            Instruction instruction = CurrentContext.CurrentInstruction;
            uint api_hash = instruction.Operand.Length == 4
                ? instruction.TokenU32
                : instruction.TokenString.ToInteropMethodHash();
            long price = Service.GetPrice(api_hash);
            if (price > 0) return price;
            if (api_hash == "Neo.Asset.Create".ToInteropMethodHash() ||
               api_hash == "AntShares.Asset.Create".ToInteropMethodHash())
                return 5000L * 100000000L / ratio;
            if (api_hash == "Neo.Asset.Renew".ToInteropMethodHash() ||
                api_hash == "AntShares.Asset.Renew".ToInteropMethodHash())
                return (byte)CurrentContext.EvaluationStack.Peek(1).GetBigInteger() * 5000L * 100000000L / ratio;
            if (api_hash == "Neo.Contract.Create".ToInteropMethodHash() ||
                api_hash == "Neo.Contract.Migrate".ToInteropMethodHash() ||
                api_hash == "AntShares.Contract.Create".ToInteropMethodHash() ||
                api_hash == "AntShares.Contract.Migrate".ToInteropMethodHash())
            {
                long fee = 100L;

                ContractPropertyState contract_properties = (ContractPropertyState)(byte)CurrentContext.EvaluationStack.Peek(3).GetBigInteger();

                if (contract_properties.HasFlag(ContractPropertyState.HasStorage))
                {
                    fee += 400L;
                }
                if (contract_properties.HasFlag(ContractPropertyState.HasDynamicInvoke))
                {
                    fee += 500L;
                }
                return fee * 100000000L / ratio;
            }
            if (api_hash == "System.Storage.Put".ToInteropMethodHash() ||
                api_hash == "System.Storage.PutEx".ToInteropMethodHash() ||
                api_hash == "Neo.Storage.Put".ToInteropMethodHash() ||
                api_hash == "AntShares.Storage.Put".ToInteropMethodHash())
                return ((CurrentContext.EvaluationStack.Peek(1).GetByteArray().Length + CurrentContext.EvaluationStack.Peek(2).GetByteArray().Length - 1) / 1024 + 1) * 1000;
            return 1;
        }

        protected override bool PreExecuteInstruction()
        {
            if (CurrentContext.InstructionPointer >= CurrentContext.Script.Length)
                return true;
            gas_consumed = checked(gas_consumed + GetPrice() * ratio);
            if (!testMode && gas_consumed > gas_amount)
            {
                if (DumpInfo != null) DumpInfo.Error("gas_consumed > gas_amount");
                return false;
            }
            if (!CheckDynamicInvoke())
            {
                if (DumpInfo != null) DumpInfo.Error("CheckDynamicInvoke");
                return false;
            }
            return true;
        }

        public static ApplicationEngine Run(byte[] script, Snapshot snapshot,
            IScriptContainer container = null, Block persistingBlock = null, bool testMode = false, Fixed8 extraGAS = default(Fixed8), bool beginDebug = false)
        {
            snapshot.PersistingBlock = persistingBlock ?? snapshot.PersistingBlock ?? new Block
            {
                Version = 0,
                PrevHash = snapshot.CurrentBlockHash,
                MerkleRoot = new UInt256(),
                Timestamp = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.Timestamp + Blockchain.SecondsPerBlock,
                Index = snapshot.Height + 1,
                ConsensusData = 0,
                NextConsensus = snapshot.Blocks[snapshot.CurrentBlockHash].TrimmedBlock.NextConsensus,
                Witness = new Witness
                {
                    InvocationScript = new byte[0],
                    VerificationScript = new byte[0]
                },
                Transactions = new Transaction[0]
            };
            ApplicationEngine engine = new ApplicationEngine(TriggerType.Application, container, snapshot, extraGAS, testMode);
            if (beginDebug)
                engine.BeginDebug();
            engine.LogScript(script);
            engine.LoadScript(script);
            engine.Execute();
            return engine;
        }
        public static ApplicationEngine RunWithDebug(byte[] script, IScriptContainer container = null, Block persistingBlock = null, bool testMode = false, Fixed8 extraGAS = default(Fixed8))
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                return Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }

        public static ApplicationEngine Run(byte[] script, IScriptContainer container = null, Block persistingBlock = null, bool testMode = false, Fixed8 extraGAS = default(Fixed8))
        {
            using (Snapshot snapshot = Blockchain.Singleton.GetSnapshot())
            {
                return Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }

        public Neo.SmartContract.Debug.DumpInfo DumpInfo
        {
            get;
            private set;
        }
        public void BeginDebug()
        {//´ò¿ªLog
            this.DumpInfo = new DumpInfo();
        }

        public override void SetParam(OpCode opcode, byte[] opdata)
        {
            if (this.DumpInfo != null)
                this.DumpInfo.SetParam(opcode, opdata);
        }

        public override void LogScript(byte[] script)
        {
            if (this.DumpInfo != null)
            {
                var hash = script.ToScriptHash().ToString();
                this.DumpInfo.LoadScript(hash);
            }
            base.LogScript(script);
        }

        void LogResult(VM.OpCode nextOpcode, VM.ExecutionStackRecord.Op[] records, VM.StackItem lastrecord)
        {
            if (records != null && records.Length > 0)
            {
                this.DumpInfo.OPStackRecord(records.ToArray());
            }
            if (lastrecord != null)
            {
                this.DumpInfo.OpResult(lastrecord);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool CheckArraySize(int length)
        {
            var b = base.CheckArraySize(length);
            if (!b && DumpInfo != null)
            {
                DumpInfo.Error("CheckArraySize");
            }
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool CheckMaxItemSize(int length)
        {
            var b = base.CheckMaxItemSize(length);
            if (!b && DumpInfo != null)
            {
                DumpInfo.Error("CheckMaxItemSize");
            }
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool CheckBigInteger(BigInteger value)
        {
            var b = base.CheckBigInteger(value);
            if (!b && DumpInfo != null)
            {
                DumpInfo.Error("CheckMaxItemSize");
            }
            return b;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool CheckStackSize(bool strict, int count = 1)
        {
            var b = base.CheckStackSize(strict, count);
            if (!b && DumpInfo != null)
            {
                DumpInfo.Error("CheckMaxItemSize");
            }
            return b;
        }
    }
}
