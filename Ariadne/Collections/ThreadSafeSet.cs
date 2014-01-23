// © 2011–2014 Jon Hanna.
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

// The algorithm here is a simplification of that used for the ThreadSafeDictionary class,
// but excluding the work necessary to handle the value part of key-value pairs.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Permissions;
using System.Threading;
using SpookilySharp;

namespace Ariadne.Collections
{
    /// <summary>A hash-based set which is thread-safe for all operations, without locking.</summary>
    /// <typeparam name="T">The type of the values stored.</typeparam>
    /// <threadsafety static="true" instance="true"/>
    [Serializable]
    public sealed class ThreadSafeSet<T> : ISet<T>, ICloneable, ISerializable
    {
        private const int ReprobeLowerBound = 5;
        private const int ReprobeShift = 5;
        private const int ZeroHash = 0x55555555;
        private const int CopyChunk = 1024;
        internal class Box
        {
            public readonly T Value;
            public Box(T value)
            {
                Value = value;
            }
            public Box StripPrime()
            {
                var prime = this as PrimeBox;
                return prime == null ? this : prime.Original;
            }
        }
        internal sealed class PrimeBox : Box
        {
            public readonly Box Original;
            public PrimeBox(Box box)
                : base(box.Value)
            {
                Original = box;
            }
        }
        internal sealed class TombstoneBox : Box
        {
            public TombstoneBox(T value)
                : base(value)
            {
            }
        }
        private static readonly TombstoneBox DeadItem = new TombstoneBox(default(T));
        private struct Record
        {
            public int Hash;
            public Box Box;
        }
        [SuppressMessage("Microsoft.StyleCop.CSharp.MaintainabilityRules",
            "SA1401:FieldsMustBePrivate", Justification = "Need to be able to CAS it.")]
        private sealed class Table
        {
            public readonly Record[] Records;
            public readonly Counter Size;
            public readonly Counter Slots = new Counter();
            public readonly int Capacity;
            public readonly int Mask;
            public readonly int PrevSize;
            public readonly int ReprobeLimit;
            public Table Next;
            private int _copyIdx;
            private int _resizers;
            private int _copyDone;
            public Table(int capacity, Counter size)
            {
                Records = new Record[Capacity = capacity];
                Mask = capacity - 1;
                ReprobeLimit = (capacity >> ReprobeShift) + ReprobeLowerBound;
                PrevSize = Size = size;
            }
            public bool AllCopied
            {
                get
                {
                    Debug.Assert(_copyDone <= Capacity, "More recorded as copied than exist.");
                    return _copyDone == Capacity;
                }
            }
            public bool MarkCopied(int countCopied)
            {
                Debug.Assert(_copyDone <= Capacity, "More recorded as copied than exist.");
                return countCopied != 0 && Interlocked.Add(ref _copyDone, countCopied) == Capacity;
            }
            public bool MarkCopied()
            {
                Debug.Assert(_copyDone <= Capacity, "More recorded as copied than exist.");
                return Interlocked.Increment(ref _copyDone) == Capacity;
            }
            public int IncResizers()
            {
                return Interlocked.Increment(ref _resizers);
            }
            public int NewCopyIndex()
            {
                return Interlocked.Add(ref _copyIdx, CopyChunk);
            }
        }
        
        private Table _table;
        private readonly IEqualityComparer<T> _cmp;
        private const int DefaultCapacity = 16;

        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="capacity">The initial capacity of the set.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the items.</param>
        public ThreadSafeSet(int capacity, IEqualityComparer<T> comparer)
        {
            if(capacity >= 0 && capacity <= 0x40000000)
            {
                Validation.NullCheck(comparer, "comparer");
                if(capacity == 0)
                    capacity = DefaultCapacity;
                else
                {
                    unchecked
                    {
                        // binary round-up
                        --capacity;
                        capacity |= capacity >> 1;
                        capacity |= capacity >> 2;
                        capacity |= capacity >> 4;
                        capacity |= capacity >> 8;
                        capacity |= capacity >> 16;
                        ++capacity;
                    }
                }
                    
                _table = new Table(capacity, new Counter());
                _cmp = comparer.WellDistributed();
            }
            else
                throw new ArgumentOutOfRangeException("capacity");
        }

        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="capacity">The initial capacity of the set.</param>
        public ThreadSafeSet(int capacity)
            : this(capacity, EqualityComparer<T>.Default)
        {
        }

        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the items.</param>
        public ThreadSafeSet(IEqualityComparer<T> comparer)
            : this(DefaultCapacity, comparer)
        {
        }

        /// <summary>Creates a new lock-free set.</summary>
        public ThreadSafeSet()
            : this(DefaultCapacity)
        {
        }
        private static int EstimateNecessaryCapacity(IEnumerable<T> collection)
        {
            if(collection != null)
            {
                // Analysis disable once EmptyGeneralCatchClause
                try
                {
                    var colT = collection as ICollection<T>;
                    if(colT != null)
                        return Math.Min(colT.Count, 1024); // let’s not go above 1024 just in case there’s only a few distinct items.
                    var col = collection as ICollection;
                    if(col != null)
                        return Math.Min(col.Count, 1024);
                }
                catch
                {
                    // if some collection throws on Count but doesn’t throw when iterated through, then well that would be
                    // pretty weird, but since our calling Count is an optimisation, we should tolerate that.
                }
                return DefaultCapacity;
            }
            throw new ArgumentNullException("collection", Strings.SetNullSourceCollection);
        }

        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> from which the set is filled upon creation.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>"/> that compares the items.</param>
        public ThreadSafeSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
            : this(EstimateNecessaryCapacity(collection), comparer)
        {
            Table table = _table;
            foreach(T item in collection)
                table = PutSingleThreaded(table, new Box(item), Hash(item), true);
            _table = table;
        }

        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> from which the set is filled upon creation.</param>
        public ThreadSafeSet(IEnumerable<T> collection)
            : this(collection, EqualityComparer<T>.Default)
        {
        }
        [SecurityCritical] 
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("cmp", _cmp, typeof(IEqualityComparer<T>));
            T[] arr = ToArray();
            info.AddValue("arr", arr);
            info.AddValue("c", arr.Length);
        }
        private ThreadSafeSet(SerializationInfo info, StreamingContext context)
            : this(info.GetInt32("c"), (IEqualityComparer<T>)info.GetValue("cmp", typeof(IEqualityComparer<T>)))
        {
            var arr = (T[])info.GetValue("arr", typeof(T[]));
            Table table = _table;
            for(int i = 0; i != arr.Length; ++i)
            {
                T item = arr[i];
                table = PutSingleThreaded(table, new Box(item), Hash(item), true);
            }
            _table = table;
        }
        private int Hash(T item)
        {
            // We must prohibit the value of zero in order to be sure that when we encounter a
            // zero, that the hash has not been written.
            // We do not use a Wang-Jenkins like Dr. Click’s approach, since .NET’s IComparer allows
            // users of the class to fix the effects of poor hash algorithms.
            int givenHash = _cmp.GetHashCode(item);
            return givenHash == 0 ? ZeroHash : givenHash;
        }
        internal Box Obtain(T item)
        {
            return Obtain(_table, item, Hash(item));
        }
        private Box Obtain(Table table, T item, int hash)
        {
            do
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                Record[] records = table.Records;
                do
                {
                    int curHash = records[idx].Hash;
                    if(curHash == hash)
                    {
                        Box box = records[idx].Box;
                        if(box == null)
                            return null;
                        T value = box.Value;
                        if(_cmp.Equals(item, value) && box != DeadItem)
                            return box is TombstoneBox ? null : box;
                    }
                    else if(curHash == 0)
                        return null;
                    else if(--reprobes == 0)
                        break;
                } while((idx = (idx + 1) & mask) != endIdx);
            } while((table = table.Next) != null);
            return null;
        }
        internal Box PutIfMatch(Box box)
        {
            return PutIfMatch(_table, box, Hash(box.Value));
        }
        private Box PutIfMatch(Table table, Box box, int hash)
        {
            TombstoneBox deadItem = DeadItem;
            for(;;)
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                Record[] records = table.Records;
                Box curBox = null;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        if(box is TombstoneBox)
                            return null;
                        if((curHash = Interlocked.CompareExchange(ref records[idx].Hash, hash, 0)) == 0)
                            curHash = hash;
                    }
                    if(curHash == hash)
                    {
                        curBox = records[idx].Box;
                        if(curBox == null)
                        {
                            if(box is TombstoneBox)
                                return null;
                            if((curBox = Interlocked.CompareExchange(ref records[idx].Box, box, null)) == null)
                            {
                                table.Slots.Increment();
                                table.Size.Increment();
                                return null;
                            }
                        }
                        if(_cmp.Equals(curBox.Value, box.Value) && curBox != deadItem)
                           break;
                    }
                    else if(--reprobes == 0)
                    {
                        Resize(table);
                        goto restart;
                    }
                    if((idx = (idx + 1) & mask) == endIdx)
                    {
                        Resize(table);
                        goto restart;
                    }
                }
                if(table.Next != null)
                {
                    CopySlotsAndCheck(table, deadItem, idx);
                    goto restart;
                }
                for(;;)
                {
                    // we have a record with a matching key.
                    if((box is TombstoneBox) == (curBox is TombstoneBox))

                        // no change, return that stored.
                        return curBox;
                    
                    Box prevBox = Interlocked.CompareExchange(ref records[idx].Box, box, curBox);
                    if(prevBox == curBox)
                    {
                        if(box is TombstoneBox)
                        {
                            if(!(prevBox is TombstoneBox))
                               table.Size.Decrement();
                        }
                        else if(prevBox is TombstoneBox)
                            table.Size.Increment();
                        return prevBox;
                    }

                    // we lost the race, another thread set the box.
                    if(prevBox == deadItem)
                        break;
                    if(prevBox is PrimeBox)
                    {
                        CopySlotsAndCheck(table, deadItem, idx);
                        break;
                    }
                    curBox = prevBox;
                }
            restart:
                HelpCopy(table, records, deadItem);
                table = table.Next;
            }
        }
        private void PutIfEmpty(Table table, Box box, int hash)
        {
            TombstoneBox deadItem = DeadItem;
            for(;;)
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                Record[] records = table.Records;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if((curHash == 0 && Interlocked.CompareExchange(ref records[idx].Hash, hash, 0) == 0) || curHash == hash)
                    {
                        Box curBox = records[idx].Box;
                        if(curBox == null && (curBox = Interlocked.CompareExchange(ref records[idx].Box, box, null)) == null)
                        {
                            table.Slots.Increment();
                            return;
                        }
                        if(_cmp.Equals(curBox.Value, box.Value) && curBox != deadItem)
                            return;
                    }
                    else if(--reprobes == 0)
                    {
                        Resize(table);
                        break;
                    }
                    else if(records[idx].Box == deadItem)
                        break;
                    if((idx = (idx + 1) & mask) == endIdx)
                    {
                        Resize(table);
                        break;
                    }
                }
                HelpCopy(table, records, deadItem);
                table = table.Next;
            }
        }
        private Table PutSingleThreaded(Table table, Box box, int hash, bool incSize)
        {
            for(;;)
            {
                int mask = table.Mask;
                int idx = hash & mask;
                int endIdx = idx;
                int reprobes = table.ReprobeLimit;
                Record[] records = table.Records;
                Table next;
                for(;;)
                {
                    int curHash = records[idx].Hash;
                    if(curHash == 0)
                    {
                        // Nothing written here
                        records[idx].Hash = hash;
                        records[idx].Box = box;
                        table.Slots.Increment();
                        if(incSize)
                            table.Size.Increment();
                        return table;
                    }
                    if(curHash == hash)
                    {
                        // Hashes match, do keys?
                        if(_cmp.Equals(records[idx].Box.Value, box.Value))
                        {
                            records[idx].Box = box;
                            return table;
                        }
                    }
                    else if(--reprobes == 0)
                    {
                        int newCap = table.Capacity << 1;
                        if(newCap < (1 << ReprobeShift))
                            newCap = 1 << ReprobeShift;
                        next = new Table(newCap, table.Size);
                        break;
                    }
                    if((idx = (idx + 1) & mask) == endIdx)
                    {
                        next = new Table(table.Capacity << 1, table.Size);
                        break;
                    }
                }
                for(idx = 0; idx != records.Length; ++idx)
                {
                    Record record = records[idx];
                    int h = record.Hash;
                    if(h != 0)
                        PutSingleThreaded(next, record.Box, h, false);
                }
                table = next;
            }
        }
        private void CopySlotsAndCheck(Table table, TombstoneBox deadItem, int idx)
        {
            if(CopySlot(table, deadItem, ref table.Records[idx]) && table.MarkCopied())
                Promote(table);
        }

        // Copy a bunch of records to the next table.
        // Copy a bunch of records to the next table.
        private void HelpCopy(Table table, Record[] records, TombstoneBox deadItem)
        {
            // Some things to note about our maximum chunk size. First, it’s a nice round number which will probably
            // result in a bunch of complete cache-lines being dealt with. It’s also big enough number that we’re not
            // at risk of false-sharing with another thread (that is, where two resizing threads keep causing each other’s
            // cache-lines to be invalidated with each write.
            int cap = table.Capacity;
            if(cap > CopyChunk)
                HelpCopyLarge(table, records, deadItem, cap);
            else
                HelpCopySmall(table, records, deadItem);
        }
        private void HelpCopyLarge(Table table, Record[] records, TombstoneBox deadItem, int capacity)
        {
            int copyIdx = table.NewCopyIndex();
            if(table != _table || table.Next.Next != null || copyIdx > capacity << 1)
                HelpCopyLargeAll(table, records, deadItem, capacity, copyIdx);
            else
                HelpCopyLargeSome(table, records, deadItem, capacity, copyIdx);
        }
        private void HelpCopyLargeAll(Table table, Record[] records, TombstoneBox deadItem, int capacity, int copyIdx)
        {
            copyIdx &= capacity - 1;
            int final = copyIdx == 0 ? capacity : copyIdx;
            while(!table.AllCopied)
            {
                int end = copyIdx + CopyChunk;
                int workDone = 0;
                while(copyIdx != end)
                    if(CopySlot(table, deadItem, ref records[copyIdx++]))
                        ++workDone;
                if(copyIdx == final || table.MarkCopied(workDone))
                {
                    Promote(table);
                    break;
                }
                if(copyIdx == capacity)
                    copyIdx = 0;
            }
        }
        private void HelpCopyLargeSome(Table table, Record[] records, TombstoneBox deadItem, int capacity, int copyIdx)
        {
            if(!table.AllCopied)
            {
                copyIdx &= capacity - 1;
                int end = copyIdx + CopyChunk;
                int workDone = 0;
                while(copyIdx != end)
                    if(CopySlot(table, deadItem, ref records[copyIdx++]))
                        ++workDone;
                if(table.MarkCopied(workDone))
                    Promote(table);
            }
        }
        private void HelpCopySmall(Table table, Record[] records, TombstoneBox deadKey)
        {
            if(!table.AllCopied)
            {
                for(int idx = 0; idx != records.Length; ++idx)
                    CopySlot(table, deadKey, ref records[idx]);
                table.MarkCopied(records.Length);
                Promote(table);
            }
        }
        private void Promote(Table table)
        {
            while(table != _table)
            {
                Table tab = _table;
                while(tab.Next != table)
                {
                    tab = tab.Next;
                    if(tab == null)
                        return;
                }
                if(Interlocked.CompareExchange(ref tab.Next, table.Next, table) == table)
                    return;
            }
            Interlocked.CompareExchange(ref _table, table.Next, table);
        }
        private bool CopySlot(Table table, TombstoneBox deadItem, ref Record record)
        {
            Box box = Interlocked.CompareExchange(ref record.Box, deadItem, null);
            return box == null || CopySlot(table, deadItem, ref record.Box, record.Hash, box, box);
        }
        private bool CopySlot(Table table, Box deadItem, ref Box boxRef, int hash, Box box, Box oldBox)
        {
            while(!(box is PrimeBox))
            {
                if(box is TombstoneBox)
                {
                    if(box == deadItem)
                        return false;
                    oldBox = Interlocked.CompareExchange(ref boxRef, deadItem, box);
                    if(oldBox == box)
                        return true;
                }
                else
                {
                    var prime = new PrimeBox(box);
                    oldBox = Interlocked.CompareExchange(ref boxRef, prime, box);
                    if(box == oldBox)
                    {
                        box = prime;
                        break;
                    }
                }
                box = oldBox;
            }
            PutIfEmpty(table.Next, oldBox.StripPrime(), hash);
            for(;;)
            {
                oldBox = Interlocked.CompareExchange(ref boxRef, deadItem, box);
                if(oldBox == box)
                    return true;
                if(oldBox == deadItem)
                    return false;
                box = oldBox;
            }
        }
        private static void Resize(Table tab)
        {
            // Heuristic is a polite word for guesswork! Almost certainly the heuristic here could be improved,
            // but determining just how best to do so requires the consideration of many different cases.
            if(tab.Next != null)
                return;
            int sz = tab.Size;
            int cap = tab.Capacity;
            int newCap;
            if(sz >= (cap * 3) >> 2)
                newCap = sz << 3;
            else if(sz >= cap >> 1)
                newCap = sz << 2;
            else if(sz >= cap >> 2)
                newCap = sz << 1;
            else
                newCap = sz;
             if(tab.Slots >= sz << 1)
                newCap = cap << 1;
            if(newCap < cap)
                newCap = cap;
            if(sz == tab.PrevSize)
                newCap <<= 1;

            unchecked
            {
                // binary round-up
                --newCap;
                newCap |= newCap >> 1;
                newCap |= newCap >> 2;
                newCap |= newCap >> 4;
                newCap |= newCap >> 8;
                newCap |= newCap >> 16;
                ++newCap;
            }

            int megabytes = newCap >> 18;
            if(megabytes > 0)
            {
                int resizers = tab.IncResizers();
                if(resizers > 2)
                {
                    if(tab.Next != null)
                        return;
                    Thread.SpinWait(20);
                    if(tab.Next != null)
                        return;
                    Thread.Sleep(Math.Max(megabytes * 5 * resizers, 200));
                }
            }
            
            if(tab.Next != null)
                return;
            
            Interlocked.CompareExchange(ref tab.Next, new Table(newCap, tab.Size), null);
        }

        /// <summary>Gets the number of items contained.</summary>
        /// <value>The number of items contained.</value>
        public int Count
        {
            get { return _table.Size; }
        }

        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }

        /// <summary>Attempts to add an item to the set.</summary>
        /// <param name="item">The item to add.</param>
        /// <returns>True if the item was added, false if a matching item was already present.</returns>
        public bool Add(T item)
        {
            Box prev = PutIfMatch(new Box(item));
            return prev != null || !(prev is TombstoneBox);
        }

        /// <summary>Attempts to add a collection of items to the set, returning those which were added.</summary>
        /// <param name="items">The items to add.</param>
        /// <returns>An enumeration of those items which where added to the set, excluding those which were already present.</returns>
        /// <remarks>The returned enumerable is lazily executed, and items are only added to the set as it is enumerated.</remarks>
        public AddedEnumeration FilterAdd(IEnumerable<T> items)
        {
            return new AddedEnumeration(this, items);
        }

        /// <summary>An enumerator that adds to the set as it is enumerated, returning only those items added.</summary>
        /// <threadsafety static="true" instance="true">This class is not thread-safe in itself, though its methods may
        /// be called concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public sealed class AddedEnumerator : IEnumerator<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private readonly IEnumerator<T> _srcEnumerator;
            private T _current;
            internal AddedEnumerator(ThreadSafeSet<T> tset, IEnumerator<T> srcEnumerator)
            {
                _set = tset;
                _srcEnumerator = srcEnumerator;
            }

            /// <summary>Gets the current element being enumerated.</summary>
            /// <value>The current element being enumerated.</value>
            public T Current
            {
                get { return _current; }
            }
            object IEnumerator.Current
            {
                get { return _current; }
            }

            /// <summary>Disposes of the enumeration, doing any necessary clean-up operations.</summary>
            public void Dispose()
            {
                _srcEnumerator.Dispose();
            }

            /// <summary>Moves to the next item in the enumeration.</summary>
            /// <returns>True if an item was found, false it the end of the enumeration was reached.</returns>
            public bool MoveNext()
            {
                while(_srcEnumerator.MoveNext())
                {
                    T current = _srcEnumerator.Current;
                    if(_set.Add(current))
                    {
                        _current = current;
                        return true;
                    }
                }
                return false;
            }

            /// <summary>Resets the enumerations.</summary>
            /// <exception cref="NotSupportedException">The source enumeration does not support resetting.</exception>
            public void Reset()
            {
                try
                {
                    _srcEnumerator.Reset();
                }
                catch(NotSupportedException nse)
                {
                    throw new NotSupportedException(Strings.ResettingNotSupportedBySource, nse);
                }
                catch(NotImplementedException)
                {
                    throw new NotSupportedException(Strings.ResettingNotSupportedBySource);
                }
            }
        }

        /// <summary>An enumeration that adds to the set as it is enumerated, returning only those items added.</summary>
        /// <threadsafety static="true" instance="true"/>
        /// <tocexclude/>
        public struct AddedEnumeration : IEnumerable<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private readonly IEnumerable<T> _srcEnumerable;
            internal AddedEnumeration(ThreadSafeSet<T> tset, IEnumerable<T> srcEnumerable)
            {
                _set = tset;
                _srcEnumerable = srcEnumerable;
            }

            /// <summary>Returns an enumerator that iterates through the collection.</summary>
            /// <returns>The enumerator.</returns>
            public AddedEnumerator GetEnumerator()
            {
                return new AddedEnumerator(_set, _srcEnumerable.GetEnumerator());
            }
            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>Attempts to add a collection of items to the set, returning the number added.</summary>
        /// <param name="items">The items to add.</param>
        /// <returns>The number of items added, excluding those which were already present.</returns>
        public int AddRange(IEnumerable<T> items)
        {
            int count = 0;
            using(var en = FilterAdd(items).GetEnumerator())
                while(en.MoveNext())
                    ++count;
            return count;
        }

        /// <summary>Modifies the current set so that it contains all elements that are present in both the current set and in the specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public void UnionWith(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            if(other != this)
                foreach(T item in other)
                    Add(item);
        }

        /// <summary>Modifies the current set so that it contains only elements that are also in a specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public void IntersectWith(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            if(other != this && Count != 0)
            {
                var copyTo = new ThreadSafeSet<T>(_table.Capacity, _cmp);
                foreach(T item in other)
                    if(Contains(item))
                        copyTo.Add(item);
                Thread.MemoryBarrier();
                _table = copyTo._table;
            }
        }

        /// <summary>Removes all elements in the specified collection from the current set.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public void ExceptWith(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            if(other == this)
                Clear();
            else
                foreach(T item in other)
                    Remove(item);
        }

        /// <summary>Modifies the current set so that it contains only elements that are present either in the current set or in the specified collection, but not both.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            if(other == this)
                Clear();
            else if(Count == 0)
                UnionWith(other);
            else
                foreach(T item in other)
                    if(!Remove(item))
                        Add(item);
        }

        /// <summary>Determines whether a set is a subset of a specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the set is a subset of the parameter, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            int count = Count;
            if(count == 0)
                return true;
            var asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count < count)
                    return false;
                var asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && asLFHS._cmp.Equals(_cmp))
                    return asLFHS.IsSupersetOf(this);
            }
            int countBoth = 0;
            foreach(T item in other)
                if(Contains(item))
                    ++countBoth;
            return countBoth == count;
        }

        /// <summary>Determines whether the current set is a superset of a specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the set is a superset of the parameter, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            var asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count == 0)
                    return true;

                // We can only short-cut on other being larger if larger is a set
                // with the same equality comparer, as otherwise two or more items
                // could be considered a single item to this set.
                var asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                    return false;
                var asHS = other as HashSet<T>;
                if(asHS != null && _cmp.Equals(asHS.Comparer) && asHS.Count > Count)
                    return false;
            }
            foreach(T item in other)
                if(!Contains(item))
                    return false;
            return true;
        }

        /// <summary>Determines whether the current set is a correct superset of a specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the set is a proper superset of the parameter, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            var asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count == 0)
                    return true;

                // We can only short-cut on other being larger if larger is a set
                // with the same equality comparer, as otherwise two or more items
                // could be considered a single item to this set.
                var asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                    return false;
                var asHS = other as HashSet<T>;
                if(asHS != null && _cmp.Equals(asHS.Comparer) && asHS.Count > Count)
                    return false;
            }
            int matched = 0;
            foreach(T item in other)
                if(Contains(item))
                    ++matched;
                else
                    return false;
            return matched < Count;
        }

        /// <summary>Determines whether the current set is a property (strict) subset of a specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the set is a proper subset of the parameter, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            int count = Count;
            if(count == 0)
                return true;
            var asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count < count)
                    return false;
                var asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && asLFHS._cmp.Equals(_cmp))
                    return asLFHS.IsProperSupersetOf(this);
            }
            int countBoth = 0;
            bool notInThis = false;
            foreach(T item in other)
                if(Contains(item))
                    ++countBoth;
                else
                    notInThis = true;
            return notInThis && countBoth == count;
        }

        /// <summary>Determines whether the current set overlaps with the specified collection.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the sets have at least one item in common, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool Overlaps(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            if(Count != 0)
                foreach(T item in other)
                    if(Contains(item))
                        return true;
            return false;
        }

        /// <summary>Determines whether the current set and the specified collection contain the same elements.</summary>
        /// <param name="other">The collection to compare to the current set.</param>
        /// <returns>True if the sets have the same items, false otherwise.</returns>
        /// <remarks>As this method will operate without locking, additions and removals from other threads may result in inconsistent results. For most
        /// purposes it will be only be useful while the collection is being operated upon by only one thread (perhaps before or after unlocked multi-threaded
        /// use).</remarks>
        public bool SetEquals(IEnumerable<T> other)
        {
            Validation.NullCheck(other, "other");
            int asSetCount = -1;
            var asLFHS = other as ThreadSafeSet<T>;
            if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                asSetCount = asLFHS.Count;
            else
            {
                var asHS = other as HashSet<T>;
                if(asHS != null && _cmp.Equals(asHS.Comparer) && asHS.Count > Count)
                    asSetCount = asHS.Count;
            }
            if(asSetCount != -1)
            {
                if(Count != asSetCount)
                    return false;
                foreach(T item in other)
                    if(!Contains(item))
                        return false;
                return true;
            }
            int matched = 0;
            foreach(T item in other)
                if(Contains(item))
                    ++matched;
                else
                    return false;
            return matched == Count;
        }
        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        /// <summary>Removes all items from the set.</summary>
        /// <remarks>All items are removed in a single atomic operation.</remarks>
        public void Clear()
        {
            Interlocked.Exchange(ref _table, new Table(DefaultCapacity, new Counter()));
        }

        /// <summary>Determines whether an item is present in the set.</summary>
        /// <param name="item">The item sought.</param>
        /// <returns>True if the item is found, false otherwise.</returns>
        public bool Contains(T item)
        {
            return Obtain(item) != null;
        }

        /// <summary>Copies the contents of the set to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array at which to start copying.</param>
        /// <exception cref="ArgumentNullException">The array was null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The array index was less than zero.</exception>
        /// <exception cref="ArgumentException">The number of items in the collection was too great to copy into the
        /// array at the index given.</exception>
        public void CopyTo(T[] array, int arrayIndex)
        {
            Validation.CopyTo(array, arrayIndex);
            ToHashSet().CopyTo(array, arrayIndex);
        }

        /// <summary>Copies the contents of the set to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <exception cref="ArgumentNullException">The array was null.</exception>
        /// <exception cref="ArgumentException">The number of items in the collection was too great to copy into the
        /// array.</exception>
        public void CopyTo(T[] array)
        {
            CopyTo(array, 0);
        }

        /// <summary>Removes an item from the set.</summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was removed, false if it was not found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negligible, but it should be noted
        /// that <see cref="OutOfMemoryException"/> exceptions are possible in memory-critical situations.
        /// </remarks>
        public bool Remove(T item)
        {
            T dummy;
            return Remove(item, out dummy);
        }

        /// <summary>Removes an item from the set.</summary>
        /// <param name="item">The item to remove.</param>
        /// <param name="removed">Upon returning, the item removed.</param>
        /// <returns>True if an item was removed, false if no matching item was found.</returns>
        public bool Remove(T item, out T removed)
        {
            Box prev = PutIfMatch(new TombstoneBox(item));
            if(prev == null || prev is TombstoneBox)
            {
                removed = default(T);
                return false;
            }
            removed = prev.Value;
            return true;
        }

        /// <summary>Removes items from the set that match a predicate.</summary>
        /// <param name="predicate">A <see cref="System.Func&lt;T, TResult>"/> that returns true for the items that should be removed.</param>
        /// <returns>A <see cref="System.Collections.Generic.IEnumerable&lt;T>"/> of the items removed.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negligible, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.
        /// <para>The returned enumerable is lazily executed, and items are only removed from the dictionary as it is enumerated.</para></remarks>
        public RemovingEnumeration RemoveWhere(Func<T, bool> predicate)
        {
            return new RemovingEnumeration(this, predicate);
        }

        /// <summary>Enumerates a <see cref="ThreadSafeSet&lt;T>"/>, returning items that match a predicate,
        /// and removing them from the dictionary.</summary>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public class RemovingEnumerator : IEnumerator<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private readonly Counter _size;
            private readonly Func<T, bool> _predicate;
            private Table _table;
            private int _idx;
            private T _current;
            internal RemovingEnumerator(ThreadSafeSet<T> targetSet, Func<T, bool> predicate)
            {
                _size = (_table = (_set = targetSet)._table).Size;
                _predicate = predicate;
                _idx = -1;
            }

            /// <summary>Gets the current item being enumerated.</summary>
            /// <value>The current item being enumerated.</value>
            public T Current
            {
                get { return _current; }
            }
            object IEnumerator.Current
            {
                get { return _current; }
            }

            /// <summary>Moves to the next item being enumerated.</summary>
            /// <returns>True if an item is found, false if the end of the enumeration is reached.</returns>
            public bool MoveNext()
            {
                TombstoneBox deadItem = DeadItem;
                for(; _table != null; _table = _table.Next)
                {
                    Record[] records = _table.Records;
                    while(++_idx != records.Length)
                    {
                        Box box = records[_idx].Box;
                        if(box == null || box is TombstoneBox)
                            continue;
                        if(box is PrimeBox)
                            _set.CopySlotsAndCheck(_table, deadItem, _idx);
                        else
                        {
                            T value = box.Value;
                            if(_predicate(value))
                            {
                                var tomb = new TombstoneBox(value);
                                for(;;)
                                {
                                    Box oldBox = Interlocked.CompareExchange(ref records[_idx].Box, tomb, box);
                                    if(oldBox == box)
                                    {
                                        _size.Decrement();
                                        _current = value;
                                        return true;
                                    }
                                    if(oldBox is TombstoneBox)
                                        break;
                                    if(oldBox is PrimeBox)
                                    {
                                        _set.CopySlotsAndCheck(_table, deadItem, _idx);
                                        break;
                                    }
                                    if(!_predicate(value = oldBox.Value))
                                        break;
                                    box = oldBox;
                                }
                            }
                        }
                    }
                }
                return false;
            }
            void IDisposable.Dispose()
            {
            }

            /// <summary>
            /// Resets the enumerator, so it operates upon the entire set again.
            /// </summary>
            public void Reset()
            {
                _table = _set._table;
                _idx = -1;
            }
        }

        /// <summary>Enumerates a <see cref="ThreadSafeSet&lt;T>"/>, returning items that match a predicate,
        /// and removing them from the dictionary.</summary>
        /// <threadsafety static="true" instance="true"/>
        /// <tocexclude/>
        public struct RemovingEnumeration : IEnumerable<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private readonly Func<T, bool> _predicate;
            internal RemovingEnumeration(ThreadSafeSet<T> targetSet, Func<T, bool> predicate)
            {
                _set = targetSet;
                _predicate = predicate;
            }

            /// <summary>Returns the enumeration itself, used with for-each constructs as this object serves as both enumeration and enumerator.</summary>
            /// <returns>The enumeration itself.</returns>
            public RemovingEnumerator GetEnumerator()
            {
                return new RemovingEnumerator(_set, _predicate);
            }
            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return GetEnumerator();
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>Removes all items that match a predicate.</summary>
        /// <param name="predicate">A <see cref="System.Func&lt;T, TResult>"/> that returns true when passed an item that should be removed.</param>
        /// <returns>The number of items removed.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negligible, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public int Remove(Func<T, bool> predicate)
        {
            int total = 0;
            var remover = new RemovingEnumerator(this, predicate);
            while(remover.MoveNext())
                ++total;
            return total;
        }
        internal struct BoxEnumerator
        {
            private readonly ThreadSafeSet<T> _set;
            private Table _tab;
            private Box _current;
            private int _idx;
            public BoxEnumerator(ThreadSafeSet<T> lfhs)
            {
                _tab = (_set = lfhs)._table;
                _current = null;
                _idx = -1;
            }
            public Box Current
            {
                get { return _current; }
            }
            public bool MoveNext()
            {
                for(; _tab != null; _tab = _tab.Next, _idx = -1)
                {
                    Record[] records = _tab.Records;
                    for(++_idx; _idx != records.Length; ++_idx)
                    {
                        Box box = records[_idx].Box;
                        if(box != null && !(box is TombstoneBox))
                        {
                            if(box is PrimeBox)

                                // Part-way through being copied to next table
                                // Make sure it’s there when we come to it.
                                _set.CopySlotsAndCheck(_tab, DeadItem, _idx);
                            else
                            {
                                _current = box;
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            public void Reset()
            {
                _tab = _set._table;
                _idx = -1;
            }
        }

        /// <summary>Enumerates a ThreadSafeSet&lt;T>.</summary>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        public class Enumerator : IEnumerator<T>
        {
            // Analysis disable once FieldCanBeMadeReadOnly.Local — Don’t do for mutable struct fields.
            private BoxEnumerator _src;
            internal Enumerator(BoxEnumerator src)
            {
                _src = src;
            }

            /// <summary>Gets the current item being enumerated.</summary>
            /// <value>The current item being enumerated.</value>
            public T Current
            {
                get { return _src.Current.Value; }
            }
            object IEnumerator.Current
            {
                get { return Current; }
            }
            void IDisposable.Dispose()
            {
            }

            /// <summary>Moves to the next item in the enumeration.</summary>
            /// <returns>True if another item was found, false if the end of the enumeration was reached.</returns>
            public bool MoveNext()
            {
                return _src.MoveNext();
            }

            /// <summary>Reset the enumeration.</summary>
            public void Reset()
            {
                _src.Reset();
            }
        }
        private BoxEnumerator EnumerateBoxes()
        {
            return new BoxEnumerator(this);
        }

        /// <summary>Returns an enumerator that iterates through the collection.</summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(EnumerateBoxes());
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Returns a copy of the current set.</summary>
        /// <remarks>Because this operation does not lock, the resulting set’s contents
        /// could be inconsistent in terms of an application’s use of the values.
        /// <para>If there is a value stored with a null key, it is ignored.</para></remarks>
        /// <returns>The <see cref="ThreadSafeSet&lt;T>"/>.</returns>
        public ThreadSafeSet<T> Clone()
        {
            var copy = new ThreadSafeSet<T>(Count, _cmp);
            Table copyTab = copy._table;
            for(Table table = _table; table != null; table = table.Next)
            {
                Record[] records = table.Records;
                for(int idx = 0; idx != records.Length; ++idx)
                {
                    Record record = records[idx];
                    Box box = record.Box;
                    if(box != null && !(box is TombstoneBox))
                        copyTab = PutSingleThreaded(copyTab, box.StripPrime(), record.Hash, true);
                }
            }
            copy._table = copyTab;
            return copy;
        }
        object ICloneable.Clone()
        {
            return Clone();
        }

        /// <summary>Returns a <see cref="HashSet&lt;T>"/> with the same contents and equality comparer as
        /// the lock-free set.</summary>
        /// <returns>The HashSet.</returns>
        /// <remarks>Because this operation does not lock, the resulting set’s contents
        /// could be inconsistent in terms of an application’s use of the values.</remarks>
        public HashSet<T> ToHashSet()
        {
            var hs = new HashSet<T>(_cmp);
            for(Table table = _table; table != null; table = table.Next)
            {
                Record[] records = table.Records;
                for(int idx = 0; idx != records.Length; ++idx)
                {
                    Box box = records[idx].Box;
                    if(box != null && !(box is TombstoneBox))
                    {
                        if(box is PrimeBox)

                            // Part-way through being copied to next table
                            // Make sure it’s there when we come to it.
                            CopySlotsAndCheck(table, DeadItem, idx);
                        else
                            hs.Add(box.Value);
                    }
                }
            }
            return hs;
        }

        /// <summary>Returns a <see cref="List&lt;T>"/> with the same contents as the lock-free set.</summary>
        /// <returns>A list containing the contents of the set.</returns>
        /// <remarks>Because this operation does not lock, the resulting set’s contents could be inconsistent in terms
        /// of an application’s use of the values, or include duplicate items.</remarks>
        public List<T> ToList()
        {
            return new List<T>(ToHashSet());
        }

        /// <summary>Returns an array with the same contents as the lock-free set.</summary>
        /// <returns>An array containing the contents of the set.</returns>
        /// <remarks>Because this operation does not lock, the resulting set’s contents could be inconsistent in terms
        /// of an application’s use of the values, or include duplicate items.</remarks>
        public T[] ToArray()
        {
            HashSet<T> hs = ToHashSet();
            var array = new T[hs.Count];
            hs.CopyTo(array);
            return array;
        }
        
        internal bool TryTake(out T item)
        {
            TombstoneBox deadItem = DeadItem;
            for(Table table = _table; table != null; table = table.Next)
            {
                Record[] records = table.Records;
                for(int idx = 0; idx != records.Length; ++idx)
                {
                    Box curBox = records[idx].Box;
                    if(curBox != null && !(curBox is TombstoneBox))
                    {
                        if(curBox is PrimeBox)
                        {
                            CopySlotsAndCheck(table, deadItem, idx);
                        }
                        else
                            for(;;)
                            {
                                Box prevBox = Interlocked.CompareExchange(ref records[idx].Box, new TombstoneBox(curBox.Value), curBox);
                                if(prevBox == curBox)
                                {
                                    item = curBox.Value;
                                    return true;
                                }
                                if(prevBox is TombstoneBox)
                                    break;
                                if(curBox is PrimeBox)
                                {
                                    CopySlotsAndCheck(table, deadItem, idx);
                                    break;
                                }
                                curBox = prevBox;
                            }
                    }
                }
            }
            item = default(T);
            return false;
        }
    }
}