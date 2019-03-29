using System.Numerics;
namespace Neo.Plugins
{
    public class Nep5State
    {
        public UInt160 Address;
        public UInt160 AssetHash;
        public BigInteger Balance;
        public uint LastUpdatedBlock;
    }
}
