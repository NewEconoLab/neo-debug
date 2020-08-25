using Neo.SmartContract.Dump;
using Neo.VM;
using Neo.VM.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Neo.SmartContract
{
     partial class ApplicationEngine
    {
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
                this.DumpInfo.NextOp(CurrentContext.InstructionPointer, curOpcode, tokenU32);
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
    }
}
