// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

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
