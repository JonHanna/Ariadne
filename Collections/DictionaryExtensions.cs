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
using System.Collections.Generic;
using System.Threading;

namespace Ariadne.Collections
{
    /// <summary>Provides further static methods for manipulating <see cref="LockFreeDictionary&lt;TKey, TValue>"/>’s with
    /// particular value types. In C♯ and VB.NET these extension methods can be called as instance methods on
    /// appropriately typed <see cref="LockFreeDictionary&lt;TKey, TValue>"/>s.</summary>
    public static class DictionaryExtensions
    {
        private static bool Increment<T>(LockFreeDictionary<T, int> dict, LockFreeDictionary<T, int>.Table table, T key, int hash, out int result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, int>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, int>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, int>.PrimeKV prime = pair as LockFreeDictionary<T, int>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, int>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Increment(ref records[idx].KeyValue.Value);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically increments the <see cref="int"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="result">The result of incrementing the value.</param>
        /// <returns>True if the value was found and incremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Increment<T>(this LockFreeDictionary<T, int> dict, T key, out int result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Increment(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically increments the <see cref="int"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <returns>The result of incrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Increment<T>(this LockFreeDictionary<T, int> dict, T key)
        {
            int ret;
            if(!dict.Increment(key, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
        private static bool Increment<T>(LockFreeDictionary<T, long> dict, LockFreeDictionary<T, long>.Table table, T key, int hash, out long result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, long>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, long>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, long>.PrimeKV prime = pair as LockFreeDictionary<T, long>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, long>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Increment(ref records[idx].KeyValue.Value);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically increments the <see cref="long"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="result">The result of incrementing the value.</param>
        /// <returns>True if the value was found and incremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Increment<T>(this LockFreeDictionary<T, long> dict, T key, out long result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Increment(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically increments the <see cref="long"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <returns>The result of incrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Increment<T>(this LockFreeDictionary<T, long> dict, T key)
        {
            long ret;
            if(!dict.Increment(key, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
        private static bool Decrement<T>(LockFreeDictionary<T, int> dict, LockFreeDictionary<T, int>.Table table, T key, int hash, out int result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, int>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, int>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, int>.PrimeKV prime = pair as LockFreeDictionary<T, int>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, int>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Decrement(ref records[idx].KeyValue.Value);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically decrements the <see cref="int"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <param name="result">The result of decrementing the value.</param>
        /// <returns>True if the value was found and decremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Decrement<T>(this LockFreeDictionary<T, int> dict, T key, out int result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Decrement(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically decrements the <see cref="int"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <returns>The result of decrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Decrement<T>(this LockFreeDictionary<T, int> dict, T key)
        {
            int ret;
            if(!dict.Decrement(key, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
        private static bool Decrement<T>(LockFreeDictionary<T, long> dict, LockFreeDictionary<T, long>.Table table, T key, int hash, out long result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, long>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, long>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, long>.PrimeKV prime = pair as LockFreeDictionary<T, long>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, long>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Decrement(ref records[idx].KeyValue.Value);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically decrements the <see cref="long"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <param name="result">The result of decrementing the value.</param>
        /// <returns>True if the value was found and decremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Decrement<T>(this LockFreeDictionary<T, long> dict, T key, out long result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Decrement(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically decrements the <see cref="long"/> value identified by the key, by one.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <returns>The result of decrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Decrement<T>(this LockFreeDictionary<T, long> dict, T key)
        {
            long ret;
            if(!dict.Decrement(key, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
        private static bool Plus<T>(LockFreeDictionary<T, long> dict, LockFreeDictionary<T, long>.Table table, T key, int hash, long addend, out long result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, long>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, long>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, long>.PrimeKV prime = pair as LockFreeDictionary<T, long>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, long>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Add(ref records[idx].KeyValue.Value, addend);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, long>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically adds the supplied <see cref="long"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <param name="result">The result of adding the values.</param>
        /// <returns>True if the value was found and the addition performed, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Plus<T>(this LockFreeDictionary<T, long> dict, T key, long addend, out long result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Plus(dict, dict._table, key, dict.Hash(key), addend, out result);
        }
        /// <summary>Atomically adds the supplied <see cref="long"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <returns>The result of adding the values.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Plus<T>(this LockFreeDictionary<T, long> dict, T key, long addend)
        {
            long ret;
            if(!dict.Plus(key, addend, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
        private static bool Plus<T>(LockFreeDictionary<T, int> dict, LockFreeDictionary<T, int>.Table table, T key, int hash, int addend, out int result)
        {
            for(;;)
            {
                int idx = hash & table.Mask;
                int reprobeCount = 0;
                int maxProbe = table.ReprobeLimit;
                LockFreeDictionary<T, int>.Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        LockFreeDictionary<T, int>.KV pair = records[idx].KeyValue;
                        if(dict._cmp.Equals(key, pair.Key))
                        {
                            LockFreeDictionary<T, int>.PrimeKV prime = pair as LockFreeDictionary<T, int>.PrimeKV;
                            if(prime != null)
                            {
                                dict.CopySlotsAndCheck(table, prime, idx);
                                table = table.Next;
                                break;
                            }
                            else if(pair is LockFreeDictionary<T, int>.TombstoneKV)
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                result = Interlocked.Add(ref records[idx].KeyValue.Value, addend);
                                return true;
                            }
                        }
                    }
                    if(++reprobeCount >= maxProbe)
                    {
                        LockFreeDictionary<T, int>.Table next = table.Next;
                        if(next != null)
                        {
                            table = next;
                            break;
                        }
                        result = 0;
                        return false;
                    }
                    idx = (idx + 1) & table.Mask;
                }
            }
        }
        /// <summary>Atomically adds the supplied <see cref="int"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <param name="result">The result of adding the values.</param>
        /// <returns>True if the value was found and the addition performed, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Plus<T>(this LockFreeDictionary<T, int> dict, T key, int addend, out int result)
        {
            if(dict == null)
                throw new ArgumentNullException("dict");
            return Plus(dict, dict._table, key, dict.Hash(key), addend, out result);
        }
        /// <summary>Atomically adds the supplied <see cref="int"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <param name="dict">The <see cref="LockFreeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <returns>The result of adding the values.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Plus<T>(this LockFreeDictionary<T, int> dict, T key, int addend)
        {
            int ret;
            if(!dict.Plus(key, addend, out ret))
                throw new KeyNotFoundException();
            return ret;
        }
    }
}
