// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

// A compiled binary is available from <http://hackcraft.github.com/Ariadne/> which if
// unmodified, may be used without restriction. (This dual-licensing is to provide a clear
// answer to the question of whether using the library in an application counts as creating
// a derivative work).

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ariadne
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct OffsetInt
    {
        //We want the offset to be Cache-line-size - sizeof(int).
        //On most chips that .NET or Mono run on at the moment, this would be 64 - 4 = 60.
        //On Itanium, it would be 128 - 4 = 124. Other sizes are both possible and quite likely
        //to appear in the future. We could P-Invoke and find the precise size to use (and use
        //an array of ints with gaps decided on that basis), but that wouldn't be portable, so we
        //just assume that 64 is the cache-line size and while we don't improve as much as we
        //can on Itanium, we'll at least improve things.
        [FieldOffset(60)]
        public int Num;
    }
    //about 20% slower on single-thread, much faster on multi-thread than Interlocked.Increment.
    public class Counter
    {
        private static readonly int CoreCount = EstimateCoreCount();
        private static int EstimateCoreCount()
        {
            try
            {
                return Environment.ProcessorCount;
            }
            catch
            {
                return 4;
            }
        }
        private readonly OffsetInt[] counters;
        private readonly int mask;
        public Counter()
        {
            //We won't go above 32 so that the total array size (including overhead) fits in a 4KiB page.
            //We won't go below 16 so we've a good spread.
            int size = EstimateCoreCount <= 4 ? 16 : 32;
            unchecked // binary round-up
            {
                --size;
                size |= (size >> 1);
                size |= (size >> 2);
                size |= (size >> 4);
                size |= (size >> 8);
                size |= (size >> 16);
                ++size;
            }
            mask = size - 1;
            counters = new OffsetInt[size];
        }
        public int Value
        {
            get
            {
                int sum = 0;
                for(int i = 0; i != counters.Length; ++i)
                    sum += counters[i].Num;
                return sum;
            }
        }
	    public static implicit operator int(Counter c)
	    {
	        return c.Value;
	    }
        public void Increment()
        {
            //We avoid different cores hitting the same counter, but don't completely prohibit it, so we
            //still need Interlocked.
            Interlocked.Increment(ref counters[Thread.CurrentThread.ManagedThreadId & mask].Num);
        }
        public void Decrement()
        {
            Interlocked.Decrement(ref counters[Thread.CurrentThread.ManagedThreadId & mask].Num);
        }
        public static Counter operator ++(Counter c)
        {
            c.Increment();
            return c;
        }
        public static Counter operator --(Counter c)
        {
            c.Decrement();
            return c;
        }
    }
}