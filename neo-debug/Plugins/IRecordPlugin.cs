using System;
using System.Collections.Generic;
using System.Text;

namespace Neo.Plugins
{
    public interface IRecordPlugin
    {
        void Record(object message);
    }
}
