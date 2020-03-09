using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract.Dump;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Array = System.Array;

namespace Neo.SmartContract
{
    public partial class ApplicationEngine : ExecutionEngine
    {
        public static event EventHandler<NotifyEventArgs> Notify;
        public static event EventHandler<LogEventArgs> Log;

        public const long GasFree = 0;
        private readonly long gas_amount;
        private readonly bool testMode;
        private readonly List<NotifyEventArgs> notifications = new List<NotifyEventArgs>();
        private readonly List<IDisposable> disposables = new List<IDisposable>();

        public TriggerType Trigger { get; }
        public IVerifiable ScriptContainer { get; }
        public StoreView Snapshot { get; }
        public long GasConsumed { get; private set; } = 0;
        public UInt160 CurrentScriptHash => CurrentContext?.GetState<ExecutionContextState>().ScriptHash;
        public UInt160 CallingScriptHash => CurrentContext?.GetState<ExecutionContextState>().CallingScriptHash;
        public UInt160 EntryScriptHash => EntryContext?.GetState<ExecutionContextState>().ScriptHash;
        public IReadOnlyList<NotifyEventArgs> Notifications => notifications;
        internal Dictionary<UInt160, int> InvocationCounter { get; } = new Dictionary<UInt160, int>();

        public ApplicationEngine(TriggerType trigger, IVerifiable container, StoreView snapshot, long gas, bool testMode = false)
        {
            this.gas_amount = GasFree + gas;
            this.testMode = testMode;
            this.Trigger = trigger;
            this.ScriptContainer = container;
            this.Snapshot = snapshot;
        }

        internal T AddDisposable<T>(T disposable) where T : IDisposable
        {
            disposables.Add(disposable);
            return disposable;
        }

        private bool AddGas(long gas)
        {
            GasConsumed = checked(GasConsumed + gas);
            return testMode || GasConsumed <= gas_amount;
        }

        protected override void LoadContext(ExecutionContext context)
        {
            // Set default execution context state

            context.GetState<ExecutionContextState>().ScriptHash ??= ((byte[])context.Script).ToScriptHash();

            base.LoadContext(context);
        }

        public ExecutionContext LoadScript(Script script, CallFlags callFlags, int rvcount = -1)
        {
            ExecutionContext context = LoadScript(script, rvcount);
            context.GetState<ExecutionContextState>().CallFlags = callFlags;
            return context;
        }

        public override void Dispose()
        {
            foreach (IDisposable disposable in disposables)
                disposable.Dispose();
            disposables.Clear();
            base.Dispose();
        }

        protected override bool OnSysCall(uint method)
        {
            if (!AddGas(InteropService.GetPrice(method, CurrentContext.EvaluationStack)))
                return false;
            return InteropService.Invoke(this, method);
        }

        protected override bool PreExecuteInstruction()
        {
            if (CurrentContext.InstructionPointer >= CurrentContext.Script.Length)
                return true;
            return AddGas(OpCodePrices[CurrentContext.CurrentInstruction.OpCode]);
        }

        private static Block CreateDummyBlock(StoreView snapshot)
        {
            var currentBlock = snapshot.Blocks[snapshot.CurrentBlockHash];
            return new Block
            {
                Version = 0,
                PrevHash = snapshot.CurrentBlockHash,
                MerkleRoot = new UInt256(),
                Timestamp = currentBlock.Timestamp + Blockchain.MillisecondsPerBlock,
                Index = snapshot.Height + 1,
                NextConsensus = currentBlock.NextConsensus,
                Witness = new Witness
                {
                    InvocationScript = Array.Empty<byte>(),
                    VerificationScript = Array.Empty<byte>()
                },
                ConsensusData = new ConsensusData(),
                Transactions = new Transaction[0]
            };
        }

        public static ApplicationEngine Run(byte[] script, StoreView snapshot,
            IVerifiable container = null, Block persistingBlock = null, bool testMode = false, long extraGAS = default)
        {
            snapshot.PersistingBlock = persistingBlock ?? snapshot.PersistingBlock ?? CreateDummyBlock(snapshot);
            ApplicationEngine engine = new ApplicationEngine(TriggerType.Application, container, snapshot, extraGAS, testMode);
            engine.BeginDebug();
            engine.LogScript(script);
            engine.LoadScript(script);
            var state = engine.Execute();
            engine.DumpInfo.Finish(state);
            return engine;
        }

        public static ApplicationEngine Run(byte[] script, IVerifiable container = null, Block persistingBlock = null, bool testMode = false, long extraGAS = default)
        {
            using (SnapshotView snapshot = Blockchain.Singleton.GetSnapshot())
            {
                return Run(script, snapshot, container, persistingBlock, testMode, extraGAS);
            }
        }
        public DumpInfo DumpInfo
        {
            get;
            private set;
        }

        public void BeginDebug()
        {//打开Log
            this.DumpInfo = new DumpInfo();
        }

        protected override void ExecuteNext()
        {
            OpCode curOpcode = CurrentContext.CurrentInstruction.OpCode;
            uint tokenU32 = curOpcode == OpCode.SYSCALL ? CurrentContext.CurrentInstruction.TokenU32 : 0;
            if (this.DumpInfo != null)
            {
                this.DumpInfo.NextOp(CurrentContext.InstructionPointer, curOpcode,tokenU32);
                this.CurrentContext.EvaluationStack.ClearRecord();
            }
            base.ExecuteNext();
            if (DumpInfo != null && this.CurrentContext != null)
            {
                var EvaluationStackRec = this.CurrentContext.EvaluationStack;
                StackItem result = null;
                EvaluationStack.Op[] record = EvaluationStackRec.record.ToArray();
                var ltype = EvaluationStackRec.GetLastRecordType();

                if (ltype == EvaluationStack.OpType.Push)
                {
                    result = EvaluationStackRec.PeekWithoutLog();
                }
                else if (ltype == EvaluationStack.OpType.Insert)
                {
                    result = EvaluationStackRec.PeekWithoutLog(EvaluationStackRec.record.Last().ind);
                }
                else if (ltype == EvaluationStack.OpType.Set)
                {
                    result = EvaluationStackRec.PeekWithoutLog(EvaluationStackRec.record.Last().ind);
                }
                else if (ltype == EvaluationStack.OpType.Peek)
                {
                    result = EvaluationStackRec.PeekWithoutLog();
                }
                LogResult(curOpcode, record, result);
            }
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

        private void LogResult(VM.OpCode nextOpcode, EvaluationStack.Op[] records, StackItem lastrecord)
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

        internal void SendLog(UInt160 script_hash, string message)
        {
            LogEventArgs log = new LogEventArgs(ScriptContainer, script_hash, message);
            Log?.Invoke(this, log);
        }

        internal void SendNotification(UInt160 script_hash, StackItem state)
        {
            NotifyEventArgs notification = new NotifyEventArgs(ScriptContainer, script_hash, state);
            Notify?.Invoke(this, notification);
            notifications.Add(notification);
        }

        public bool TryPop(out string s)
        {
            if (TryPop(out ReadOnlySpan<byte> b))
            {
                s = Encoding.UTF8.GetString(b);
                return true;
            }
            else
            {
                s = default;
                return false;
            }
        }
    }
}
