// © 2012–2014 Jon Hanna.
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System.Threading;

namespace Ariadne
{
    /// <summary>A simple means to share an atomically-maintained count between objects.</summary>
    /// <threadsafety static="true" instance="true"/>
    public sealed class SharedInt32
    {
        private int _value;

        /// <summary>Initialises a new instance of the <see cref="Ariadne.SharedInt32"/> class, with a value of zero.
        /// </summary>
        public SharedInt32()
        {
        }

        /// <summary>Initialises a new instance of the <see cref="Ariadne.SharedInt32"/> class, with an initial value.
        /// </summary>
        /// <param name="value">The initial value of the <see cref="Ariadne.SharedInt32"/>.</param>
        public SharedInt32(int value)
        {
            _value = value;
        }

        /// <summary>Gets the integer value.</summary>
        /// <value>The integer value.</value>
        public int Value
        {
            get { return _value; }
        }

        /// <summary>Returns the value of the <see cref="Ariadne.SharedInt32"/>.</summary>
        /// <param name="ri">The <see cref="Ariadne.SharedInt32"/> to cast.</param>
        /// <returns>An integer of the same value as the <see cref="Ariadne.SharedInt32"/>.</returns>
        public static implicit operator int(SharedInt32 ri)
        {
            return ri._value;
        }

        /// <summary>Atomically increment the value of the <see cref="Ariadne.SharedInt32"/> by one.</summary>
        /// <returns>The new value.</returns>
        public int Increment()
        {
            return Interlocked.Increment(ref _value);
        }

        /// <summary>Atomically decrement the value of the <see cref="Ariadne.SharedInt32"/> by one.</summary>
        /// <returns>The new value.</returns>
        public int Decrement()
        {
            return Interlocked.Decrement(ref _value);
        }

        /// <summary>Atomically add a value to the <see cref="Ariadne.SharedInt32"/>.</summary>
        /// <param name="addend">The number to add to the <see cref="Ariadne.SharedInt32"/>.</param>
        /// <returns>The new value.</returns>
        public int Add(int addend)
        {
            return Interlocked.Add(ref _value, addend);
        }

        /// <summary>Atomically replace the value of the <see cref="Ariadne.SharedInt32"/>, returning the previous value.
        /// </summary>
        /// <param name="value">The number to set the <see cref="Ariadne.SharedInt32"/> to.</param>
        /// <returns>The old value.</returns>
        public int Exchange(int value)
        {
            return Interlocked.Exchange(ref _value, value);
        }

        /// <summary>Atomically subtract a value from the <see cref="Ariadne.SharedInt32"/>.</summary>
        /// <param name="subtrahend">The number to subtract from the <see cref="Ariadne.SharedInt32"/>.</param>
        /// <returns>The new value.</returns>
        public int Subtract(int subtrahend)
        {
            return Interlocked.Add(ref _value, -subtrahend);
        }
    }
}
