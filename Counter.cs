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
        [FieldOffset(64)]
        public int Num;
    }
    /// <summary>A counter designed for highly concurrent use.</summary>
    /// <remarks>This counter tends to be appreciably slower than using <see cref="Interlocked.Increment(ref int)"/>
    /// when there is little contention. However, it is much faster in the face of contending threads, with the
    /// comparable cost of <see cref="Interlocked.Increment(ref int)"/> increasing nearly expotentially to
    /// the number of contending threads.</remarks>
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
        /// <summary>Creates a new <see cref="Counter"/> with an initial value of zero.</summary>
        public Counter()
        {
            int cores = CoreCount;
            mask = cores - 1;
            counters = new OffsetInt[cores];
        }
        /// <summary>Creates a new <see cref="Counter"/> with an initial value of <paramref name="startingValue"/>.</summary>
        /// <param name="startingValue"></param>
        public Counter(int startingValue)
            :this()
        {
            counters[0].Num = startingValue;
        }
        /// <summary>The current value of the counter.</summary>
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
        /// <summary>Returns the value of the <see cref="Counter"/>.</summary>
        /// <param name="c">The <see cref="Counter"/> to cast.</param>
        /// <returns>An integer of the same value as the <see cref="Counter"/>.</returns>
	    public static implicit operator int(Counter c)
	    {
	        return c.Value;
	    }
	    /// <summary>Atomically increments the <see cref="Counter"/> by one.</summary>
        public void Increment()
        {
            Interlocked.Increment(ref counters[(Thread.CurrentThread.ManagedThreadId) & mask].Num);
        }
        /// <summary>Atomically decrements the <see cref="Counter"/> by one.</summary>
        public void Decrement()
        {
            Interlocked.Decrement(ref counters[(Thread.CurrentThread.ManagedThreadId) & mask].Num);
        }
    }
}