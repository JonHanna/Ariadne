// © 2012 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

// A compiled binary is available from <http://hackcraft.github.com/Ariadne/> which if
// unmodified, may be used without restriction.

using System;
using System.Threading;

namespace Ariadne
{
	/// <summary>A simple means to share an atomically-maintained count between objects.</summary>
	/// <threadsafety static="true" instance="true"/>
	public sealed class SharedInt
	{
	    private int _value;
	    /// <summary>Creates a new AliasedInt with a value of zero.</summary>
	    public SharedInt(){}
	    /// <summary>Creates a new AliasedInt.</summary>
	    /// <param name="value">The initial value of the object.</param>
	    public SharedInt(int value)
	    {
	        _value = value;
	    }
	    /// <summary>Returns the value of the AliasedInt.</summary>
	    public int Value
	    {
	        get { return _value; }
	    }
	    /// <summary>Returns the value of the AliasedInt.</summary>
	    /// <param name="ri">The AliasedInt to cast.</param>
	    /// <returns>An integer of the same value as the AliasedInt.</returns>
	    public static implicit operator int(SharedInt ri)
	    {
	        return ri._value;
	    }
	    /// <summary>Atomically increment the value of the AliasedInt by one.</summary>
	    /// <returns>The new value.</returns>
	    public int Increment()
	    {
	        return Interlocked.Increment(ref _value);
	    }
	    /// <summary>Atomically decrement the value of the AliasedInt by one.</summary>
	    /// <returns>The new value.</returns>
	    public int Decrement()
	    {
	        return Interlocked.Decrement(ref _value);
	    }
	    /// <summary>Atomically add a value to the AliasedInt.</summary>
	    /// <param name="addend">The number to add to the AliasedInt.</param>
	    /// <returns>The new value.</returns>
	    public int Add(int addend)
	    {
	        return Interlocked.Add(ref _value, addend);
	    }
	    /// <summary>Atomically replace the value of the AliasedInt, returning the previous value.</summary>
	    /// <param name="value">The number to set the AliasedInt to.</param>
	    /// <returns>The old value.</returns>
	    public int Exchange(int value)
	    {
	        return Interlocked.Exchange(ref _value, value);
	    }
	    /// <summary>Atomically subtract a value from the AliasedInt.</summary>
	    /// <param name="subtrahend">The number to subtract from the AliasedInt.</param>
	    /// <returns>The new value.</returns>
	    public int Subtract(int subtrahend)
	    {
	        return Interlocked.Add(ref _value, -subtrahend);
	    }
	}
}
