// © 2011–2012 Jon Hanna.
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
    /// <summary>Provides further static methods for manipulating <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/>’s with
    /// particular value types. In C♯ and VB.NET these extension methods can be called as instance methods on
    /// appropriately typed <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/>s.</summary>
    public static class DictionaryExtensions
    {
        private static void CheckDictNotNull<TKey, TValue>(ThreadSafeDictionary<TKey, TValue> dict)
        {
            Validation.NullCheck(dict, "dict");
        }
        private static bool Increment<TKey>(ThreadSafeDictionary<TKey, int> dict, ThreadSafeDictionary<TKey, int>.Table table, TKey key, int hash, out int result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, int>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, int>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, int>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, int>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Increment(ref pair.Value);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically increments the <see cref="int"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="result">The result of incrementing the value.</param>
        /// <returns>True if the value was found and incremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Increment<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key, out int result)
        {
            CheckDictNotNull(dict);
            return Increment(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically increments the <see cref="int"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <returns>The result of incrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Increment<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key)
        {
            int ret;
            if(dict.Increment(key, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
        private static bool Increment<TKey>(ThreadSafeDictionary<TKey, long> dict, ThreadSafeDictionary<TKey, long>.Table table, TKey key, int hash, out long result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, long>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, long>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, long>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, long>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Increment(ref pair.Value);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically increments the <see cref="long"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="result">The result of incrementing the value.</param>
        /// <returns>True if the value was found and incremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Increment<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key, out long result)
        {
            CheckDictNotNull(dict);
            return Increment(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically increments the <see cref="long"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <returns>The result of incrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Increment<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key)
        {
            long ret;
            if(dict.Increment(key, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
        private static bool Decrement<TKey>(ThreadSafeDictionary<TKey, int> dict, ThreadSafeDictionary<TKey, int>.Table table, TKey key, int hash, out int result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, int>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, int>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, int>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, int>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Decrement(ref pair.Value);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically decrements the <see cref="int"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <param name="result">The result of decrementing the value.</param>
        /// <returns>True if the value was found and decremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Decrement<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key, out int result)
        {
            CheckDictNotNull(dict);
            return Decrement(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically decrements the <see cref="int"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <returns>The result of decrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Decrement<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key)
        {
            int ret;
            if(dict.Decrement(key, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
        private static bool Decrement<TKey>(ThreadSafeDictionary<TKey, long> dict, ThreadSafeDictionary<TKey, long>.Table table, TKey key, int hash, out long result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, long>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, long>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, long>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, long>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Decrement(ref pair.Value);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically decrements the <see cref="long"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <param name="result">The result of decrementing the value.</param>
        /// <returns>True if the value was found and decremented, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Decrement<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key, out long result)
        {
            CheckDictNotNull(dict);
            return Decrement(dict, dict._table, key, dict.Hash(key), out result);
        }
        /// <summary>Atomically decrements the <see cref="long"/> value identified by the key, by one.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to decrement.</param>
        /// <returns>The result of decrementing the value.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Decrement<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key)
        {
            long ret;
            if(dict.Decrement(key, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
        private static bool Plus<TKey>(ThreadSafeDictionary<TKey, long> dict, ThreadSafeDictionary<TKey, long>.Table table, TKey key, int hash, long addend, out long result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, long>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, long>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, long>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, long>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Add(ref pair.Value, addend);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically adds the supplied <see cref="long"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <param name="result">The result of adding the values.</param>
        /// <returns>True if the value was found and the addition performed, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Plus<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key, long addend, out long result)
        {
            CheckDictNotNull(dict);
            return Plus(dict, dict._table, key, dict.Hash(key), addend, out result);
        }
        /// <summary>Atomically adds the supplied <see cref="long"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="long"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <returns>The result of adding the values.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static long Plus<TKey>(this ThreadSafeDictionary<TKey, long> dict, TKey key, long addend)
        {
            long ret;
            if(dict.Plus(key, addend, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
        private static bool Plus<TKey>(ThreadSafeDictionary<TKey, int> dict, ThreadSafeDictionary<TKey, int>.Table table, TKey key, int hash, int addend, out int result)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                ThreadSafeDictionary<TKey, int>.Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)//hash we care about, is it the key we care about?
                    {
                        var pair = records[idx].KeyValue;
                        if(pair == null)
                            break;
                        if(dict._cmp.Equals(key, pair.Key) && pair != ThreadSafeDictionary<TKey, int>.DeadKey)
                        {
                            if(!(pair is ThreadSafeDictionary<TKey, int>.TombstoneKV))
                            {
                                if(pair is ThreadSafeDictionary<TKey, int>.PrimeKV)
                                {
                                    dict.CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                result = Interlocked.Add(ref pair.Value, addend);
                                return true;
                            }
                            result = 0;
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            result = 0;
            return false;
        }
        /// <summary>Atomically adds the supplied <see cref="int"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <param name="result">The result of adding the values.</param>
        /// <returns>True if the value was found and the addition performed, false if the key was not found.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static bool Plus<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key, int addend, out int result)
        {
            CheckDictNotNull(dict);
            return Plus(dict, dict._table, key, dict.Hash(key), addend, out result);
        }
        /// <summary>Atomically adds the supplied <see cref="int"/> value to the value identified by the key, returning
        /// the result.</summary>
        /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
        /// <param name="dict">The <see cref="ThreadSafeDictionary&lt;TKey, TValue>"/> to operate on.
        /// TValue must be <see cref="int"/>.</param>
        /// <param name="key">The key that identifies the value to increment.</param>
        /// <param name="addend">The value to add to that in the dictionary.</param>
        /// <returns>The result of adding the values.</returns>
        /// <exception cref="KeyNotFoundException">The key was not found in the dictionary.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="dict"/> was null.</exception>
        public static int Plus<TKey>(this ThreadSafeDictionary<TKey, int> dict, TKey key, int addend)
        {
            int ret;
            if(dict.Plus(key, addend, out ret))
                return ret;
            throw new KeyNotFoundException();
        }
    }
}
