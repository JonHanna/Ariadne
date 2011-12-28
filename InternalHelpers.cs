using System;
using System.Threading;

namespace HackCraft.LockFree
{
    //Simple means to share an atomically-maintained count between objects.
    internal sealed class AliasedInt
    {
        private int _value;
        public static implicit operator int(AliasedInt ri)
        {
            return ri._value;
        }
        public int Increment()
        {
            return Interlocked.Increment(ref _value);
        }
        public int Decrement()
        {
            return Interlocked.Decrement(ref _value);
        }
    }
}
