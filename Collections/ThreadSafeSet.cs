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



// The algorithm here is a simplification of that used for the ThreadSafeDictionary class,
// but excluding the work necessary to handle the value part of key-value pairs.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Permissions;
using System.Threading;

namespace Ariadne.Collections
{
    /// <summary>A hash-based set which is thread-safe for all operations, without locking.</summary>
    /// <typeparam name="T">The type of the values stored.</typeparam>
    /// <threadsafety static="true" instance="true"/>
    [Serializable]
    public sealed class ThreadSafeSet<T> : ISet<T>, ICloneable, IProducerConsumerCollection<T>, ISerializable
    {
        private const int REPROBE_LOWER_BOUND = 5;
        private const int REPROBE_SHIFT = 5;
        private const int ZERO_HASH = 0x55555555;
        internal class Box
        {
            public readonly T Value;
            public Box(T value)
            {
                Value = value;
            }
            public Box StripPrime()
            {
                return this is PrimeBox ? new Box(Value) : this;
            }
        }
        internal sealed class PrimeBox : Box
        {
            public PrimeBox(T value)
                :base(value){}
        }
        internal sealed class TombstoneBox : Box
        {
            public TombstoneBox(T value)
                :base(value){}
        }
        private static readonly TombstoneBox DeadItem = new TombstoneBox(default(T));
        private struct Record
        {
            public int Hash;
            public Box Box;
        }
        private sealed class Table
        {
            public readonly Record[] Records;
            public Table Next;
            public readonly Counter Size;
            public readonly Counter Slots = new Counter();
            public readonly int Capacity;
            public readonly int Mask;
            public readonly int PrevSize;
            public readonly int ReprobeLimit;
            public int CopyIdx;
            public int Resizers;
            public int CopyDone;
            public Table(int capacity, Counter size)
            {
                Records = new Record[Capacity = capacity];
                Mask = capacity - 1;
                ReprobeLimit = (capacity >> REPROBE_SHIFT) + REPROBE_LOWER_BOUND;
                PrevSize = Size = size;
            }
            public bool AllCopied
            {
                get
                {
                    Debug.Assert(CopyDone <= Capacity);
                    return CopyDone == Capacity;
                }
            }
            public bool MarkCopied(int cCopied)
            {
                Debug.Assert(CopyDone <= Capacity);
                return cCopied != 0 && Interlocked.Add(ref CopyDone, cCopied) == Capacity;
            }
            public bool MarkCopied()
            {
                Debug.Assert(CopyDone <= Capacity);
                return Interlocked.Increment(ref CopyDone) == Capacity;
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
    	            unchecked // binary round-up
    	            {
    	                --capacity;
    	                capacity |= (capacity >> 1);
    	                capacity |= (capacity >> 2);
    	                capacity |= (capacity >> 4);
    	                capacity |= (capacity >> 8);
    	                capacity |= (capacity >> 16);
    	                ++capacity;
    	            }
            	}
                	
                _table = new Table(capacity, new Counter());
                _cmp = comparer;
            }
            else
                throw new ArgumentOutOfRangeException("capacity");
        }
        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="capacity">The initial capacity of the set.</param>
        public ThreadSafeSet(int capacity)
            :this(capacity, EqualityComparer<T>.Default){}
        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the items.</param>
        public ThreadSafeSet(IEqualityComparer<T> comparer)
            :this(DefaultCapacity, comparer){}
        /// <summary>Creates a new lock-free set.</summary>
        public ThreadSafeSet()
            :this(DefaultCapacity){}
        private static int EstimateNecessaryCapacity(IEnumerable<T> collection)
        {
        	if(collection != null)
        	{
            	try
            	{
                	ICollection<T> colT = collection as ICollection<T>;
                	if(colT != null)
                		return Math.Min(colT.Count, 1024); // let’s not go above 1024 just in case there’s only a few distinct items.
                	ICollection col = collection as ICollection;
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
    		throw new ArgumentNullException("collection", Strings.Set_Null_Source_Collection);
        }
        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> from which the set is filled upon creation.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>"/> that compares the items.</param>
        public ThreadSafeSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
            :this(EstimateNecessaryCapacity(collection), comparer)
        {
            foreach(T item in collection)
                Add(item);
        }
        /// <summary>Creates a new lock-free set.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> from which the set is filled upon creation.</param>
        public ThreadSafeSet(IEnumerable<T> collection)
            :this(collection, EqualityComparer<T>.Default){}
        [SecurityCritical] 
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter=true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("cmp", _cmp, typeof(IEqualityComparer<T>));
            T[] arr = ToArray();
            info.AddValue("arr", arr);
            info.AddValue("c", arr.Length);
        }
        private ThreadSafeSet(SerializationInfo info, StreamingContext context)
            :this(info.GetInt32("c"), (IEqualityComparer<T>)info.GetValue("cmp", typeof(IEqualityComparer<T>)))
        {
            AddRange((T[])info.GetValue("arr", typeof(T[])));
        }
        private int Hash(T item)
        {
            //We must prohibit the value of zero in order to be sure that when we encounter a
            //zero, that the hash has not been written.
            //We do not use a Wang-Jenkins like Dr. Click’s approach, since .NET’s IComparer allows
            //users of the class to fix the effects of poor hash algorithms.
            int givenHash = _cmp.GetHashCode(item);
            return givenHash == 0 ? ZERO_HASH : givenHash;
        }
        internal bool Obtain(T item, out T storedItem)
        {
            return Obtain(_table, item, Hash(item), out storedItem);
        }
        private bool Obtain(Table table, T item, int hash, out T storedItem)
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
                            break;
                        T value = box.Value;
                        if(_cmp.Equals(item, value) && box != DeadItem)
                        {
                            if(!(box is TombstoneBox))
                            {
                                if(box is PrimeBox)
                                {
                                    CopySlotsAndCheck(table, idx);
                                    break;
                                }
                                storedItem = value;
                                return true;
                            }
                            storedItem = default(T);
                            return false;
                        }
                    }
                    else if(curHash == 0 || --reprobes == 0)
                        break;
                }while((idx = (idx + 1) & mask) != endIdx);
            }while((table = table.Next) != null);
            storedItem = default(T);
            return false;
        }
        internal Box PutIfMatch(Box box, bool removing, bool emptyOnly)
        {
            return PutIfMatch(_table, box, Hash(box.Value), removing, emptyOnly);
        }
        private Box PutIfMatch(Table table, Box box, int hash, bool removing, bool emptyOnly)
        {
            Box deadItem = DeadItem;
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
                        if(!removing)
                        {
                            if((curBox = Interlocked.CompareExchange(ref records[idx].Box, box, null)) == null)
                            {
                                table.Slots.Increment();
                                if(!emptyOnly)
                                    table.Size.Increment();
                                return null;
                            }
                        }
                        else
                            curBox = records[idx].Box;
                        if(curBox == deadItem)
                            goto restart;
                        if(_cmp.Equals(curBox.Value, box.Value))
                           break;
                    }
                    else if(--reprobes == 0)
                    {
                        Resize(table);
                        goto restart;
                    }
                    else if(records[idx].Box == deadItem)
                        goto restart;
                    if((idx = (idx + 1) & mask) == endIdx)
                    {
                        Resize(table);
                        goto restart;
                    }
                }
    
                //we have a record with a matching key.
                if(emptyOnly || (box is TombstoneBox) == (curBox is TombstoneBox))
                    return curBox;//no change, return that stored.
                
                if(table.Next != null)
                {
                    CopySlotsAndCheck(table, idx);
                    goto restart;
                }
                for(;;)
                {
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
                    //we lost the race, another thread set the box.
                    if(prevBox is PrimeBox)
                    {
                        CopySlotsAndCheck(table, idx);
                        goto restart;
                    }
                    else if(prevBox == deadItem)
                        goto restart;
                    else if((box is TombstoneBox) == (prevBox is TombstoneBox))
                        return prevBox;//no change, return that stored.
                    else
                        curBox = prevBox;
                }
            restart:
                HelpCopy(table, records);
                table = table.Next;
            }
        }
        private void CopySlotsAndCheck(Table table, int idx)
        {
            if(CopySlot(table, DeadItem, ref table.Records[idx]) && table.MarkCopied())
                Promote(table);
        }
        // Copy a bunch of records to the next table.
        private void HelpCopy(Table table, Record[] records)
        {
            //Some things to note about our maximum chunk size. First, it’s a nice round number which will probably
            //result in a bunch of complete cache-lines being dealt with. It’s also big enough number that we’re not
            //at risk of false-sharing with another thread (that is, where two resizing threads keep causing each other’s
            //cache-lines to be invalidated with each write.
            int chunk = table.Capacity;
            if(chunk > 1024)
                chunk = 1024;
            TombstoneBox deadItem = DeadItem;
            if(table.AllCopied)
                return;
            int copyIdx = Interlocked.Add(ref table.CopyIdx, chunk) & table.Mask;
            int end = copyIdx + chunk;
            int workDone = 0;
            while(copyIdx != end)
                if(CopySlot(table, deadItem, ref records[copyIdx++]))
                    ++workDone;
            if(table.MarkCopied(workDone))
                Promote(table);
        }
        private void Promote(Table table)
        {
            while(table == _table && Interlocked.CompareExchange(ref _table, table.Next, table) == table && (table = table.Next).AllCopied);
        }
        private bool CopySlot(Table table, TombstoneBox deadItem, ref Record record)
        {
            return CopySlot(table, deadItem, ref record.Box, record.Hash);
        }
        private bool CopySlot(Table table, Box deadItem, ref Box boxRef, int hash)
        {
            //if unwritten-to we should be able to just mark it as dead.
            Box box = Interlocked.CompareExchange(ref boxRef, deadItem, null);
            if(box == null)
                return true;
            Box oldBox = box;
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
                    PrimeBox prime = new PrimeBox(box.Value);
                    oldBox = Interlocked.CompareExchange(ref boxRef, prime, box);
                    if(box == oldBox)
                    {
                        box = prime;
                        break;
                    }
                }
                box = oldBox;
            }
            PutIfMatch(table.Next, oldBox.StripPrime(), hash, false, true);
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
        private void Resize(Table tab)
        {
            //Heuristic is a polite word for guesswork! Almost certainly the heuristic here could be improved,
            //but determining just how best to do so requires the consideration of many different cases.
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

            unchecked // binary round-up
            {
                --newCap;
                newCap |= (newCap >> 1);
                newCap |= (newCap >> 2);
                newCap |= (newCap >> 4);
                newCap |= (newCap >> 8);
                newCap |= (newCap >> 16);
                ++newCap;
            }
            
            
            int MB = newCap >> 18;
            if(MB > 0)
            {
                int resizers = Interlocked.Increment(ref tab.Resizers);
                if(resizers > 2)
                {
                    if(tab.Next != null)
                        return;
                    Thread.SpinWait(20);
                    if(tab.Next != null)
                        return;
                    Thread.Sleep(Math.Max(MB * 5 * resizers, 200));
                }
            }
            
            if(tab.Next != null)
                return;
            
            Interlocked.CompareExchange(ref tab.Next, new Table(newCap, tab.Size), null);
        }
        /// <summary>Returns an estimate of the current number of items in the set.</summary>
        public int Count
        {
            get { return _table.Size; }
        }
        /// <summary>The current capacity of the set.</summary>
        /// <remarks>If the set is in the midst of a resize, the capacity it is resizing to is returned, ignoring other internal storage in use.</remarks>
        public int Capacity
        {
            get { return _table.Capacity; }
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
            Box prev = PutIfMatch(new Box(item), false, false);
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
            /// <summary>Returns the current item.</summary>
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
            /// <exception cref="NotSupportedException"/>The source enumeration does not support resetting (
            public void Reset()
            {
                try
                {
                    _srcEnumerator.Reset();
                }
                catch(NotSupportedException nse)
                {
                    throw new NotSupportedException(Strings.Resetting_Not_Supported_By_Source, nse);
                }
                catch(NotImplementedException)
                {
                    throw new NotSupportedException(Strings.Resetting_Not_Supported_By_Source);
                }
            }
        }
        /// <summary>An enumeration that adds to the set as it is enumerated, returning only those items added.</summary>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
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
            foreach(T item in FilterAdd(items))
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
                ThreadSafeSet<T> copyTo = new ThreadSafeSet<T>(Capacity, _cmp);
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
            ICollection<T> asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count < count)
                    return false;
                ThreadSafeSet<T> asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && asLFHS._cmp.Equals(_cmp))
                    return asLFHS.IsSupersetOf(this);
            }
            int cBoth = 0;
            foreach(T item in other)
                if(Contains(item))
                    ++cBoth;
            return cBoth == count;
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
            ICollection<T> asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count == 0)
                    return true;
                //We can only short-cut on other being larger if larger is a set
                //with the same equality comparer, as otherwise two or more items
                //could be considered a single item to this set.
                ThreadSafeSet<T> asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                    return false;
                HashSet<T> asHS = other as HashSet<T>;
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
            ICollection<T> asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count == 0)
                    return true;
                //We can only short-cut on other being larger if larger is a set
                //with the same equality comparer, as otherwise two or more items
                //could be considered a single item to this set.
                ThreadSafeSet<T> asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                    return false;
                HashSet<T> asHS = other as HashSet<T>;
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
            ICollection<T> asCol = other as ICollection<T>;
            if(asCol != null)
            {
                if(asCol.Count < count)
                    return false;
                ThreadSafeSet<T> asLFHS = other as ThreadSafeSet<T>;
                if(asLFHS != null && asLFHS._cmp.Equals(_cmp))
                    return asLFHS.IsProperSupersetOf(this);
            }
            int cBoth = 0;
            bool notInThis = false;
            foreach(T item in other)
                if(Contains(item))
                    ++cBoth;
                else
                    notInThis = true;
            return notInThis && cBoth == count;
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
            ThreadSafeSet<T> asLFHS = other as ThreadSafeSet<T>;
            if(asLFHS != null && _cmp.Equals(asLFHS._cmp) && asLFHS.Count > Count)
                asSetCount = asLFHS.Count;
            else
            {
                HashSet<T> asHS = other as HashSet<T>;
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
            T found;
            return Obtain(item, out found);
        }
        /// <summary>Copies the contents of the set to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array to start copying from</param>
        /// <exception cref="System.ArgumentNullException"/>The array was null.
        /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
        /// <exception cref="System.ArgumentException"/>The number of items in the collection was
        /// too great to copy into the array at the index given.
        public void CopyTo(T[] array, int arrayIndex)
        {
            Validation.CopyTo(array, arrayIndex);
            ToHashSet().CopyTo(array, arrayIndex);
        }
        /// <summary>Copies the contents of the set to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <exception cref="System.ArgumentNullException"/>The array was null.
        /// <exception cref="System.ArgumentException"/>The number of items in the collection was
        /// too great to copy into the array.
        public void CopyTo(T[] array)
        {
            CopyTo(array, 0);
        }
        /// <summary>Removes an item from the set.</summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was removed, false if it was not found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
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
            Box prev = PutIfMatch(new TombstoneBox(item), true, false);
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
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.
        /// <para>The returned enumerable is lazily executed, and items are only removed from the dictionary as it is enumerated.</para></remarks>
        public RemovingEnumeration RemoveWhere(Func<T, bool> predicate)
        {
            return new RemovingEnumeration(this, predicate);
        }
        public class RemovingEnumerator : IEnumerator<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private Table _table;
            private readonly Counter _size;
            private readonly Func<T, bool> _predicate;
            private int _idx;
            private T _current;
            internal RemovingEnumerator(ThreadSafeSet<T> lfSet, Func<T, bool> predicate)
            {
                _size = (_table = (_set = lfSet)._table).Size;
                _predicate = predicate;
                _idx = -1;
            }
            /// <summary>The current item being enumerated.</summary>
            public T Current
            {
                get { return _current; }
            }
            object IEnumerator.Current
            {
                get { return _current; }
            }
            /// <summary>Moves to the next item being enumerated.</summary>
            /// <returns>True if an item is found, false if the end of the enumeration is reached,</returns>
            public bool MoveNext()
            {
                for(; _table != null; _table = _table.Next)
                {
                    Record[] records = _table.Records;
                    while(++_idx != records.Length)
                    {
                        Box box = records[_idx].Box;
                        if(box == null || box is TombstoneBox)
                            continue;
                        if(box is PrimeBox)
                            _set.CopySlotsAndCheck(_table, _idx);
                        else
                        {
                            T value = box.Value;
                            if(_predicate(value))
                            {
                                TombstoneBox tomb = new TombstoneBox(value);
                                for(;;)
                                {
                                    Box oldBox = Interlocked.CompareExchange(ref records[_idx].Box, tomb, box);
                                    if(oldBox == box)
                                    {
                                        _size.Decrement();
                                        _current = value;
                                        return true;
                                    }
                                    else if(oldBox is TombstoneBox)
                                        break;
                                    else if(oldBox is PrimeBox)
                                    {
                                        _set.CopySlotsAndCheck(_table, _idx);
                                        break;
                                    }
                                    else if(!_predicate(value = oldBox.Value))
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
            public void Reset()
            {
                _table = _set._table;
                _idx = -1;
            }
        }
        /// <summary>Enumerates a <see cref="ThreadSafeSet&lt;T>"/>, returning items that match a predicate,
        /// and removing them from the dictionary.</summary>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public struct RemovingEnumeration : IEnumerable<T>
        {
            private readonly ThreadSafeSet<T> _set;
            private readonly Func<T, bool> _predicate;
            internal RemovingEnumeration(ThreadSafeSet<T> lfSet, Func<T, bool> predicate)
            {
                _set = lfSet;
                _predicate = predicate;
            }
            /// <summary>Returns the enumeration itself, used with for-each constructs as this object serves as both enumeration and eumerator.</summary>
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
        /// <returns>The number of items removed</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public int Remove(Func<T, bool> predicate)
        {
            int total = 0;
            RemovingEnumerator remover = new RemovingEnumerator(this, predicate);
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
                            if(box is PrimeBox)//part-way through being copied to next table
                                _set.CopySlotsAndCheck(_tab, _idx);//make sure it’s there when we come to it.
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
            private BoxEnumerator _src;
            internal Enumerator(BoxEnumerator src)
            {
                _src = src;
            }
            /// <summary>Returns the current item being enumerated.</summary>
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
            /// <summary>Reset the enumeration</summary>
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
            ThreadSafeSet<T> copy = new ThreadSafeSet<T>(Count, _cmp);
            for(Table table = _table; table != null; table = table.Next)
            {
                Record[] records = table.Records;
                for(int idx = 0; idx != records.Length; ++idx)
                {
                    Box box = records[idx].Box;
                    if(box != null && !(box is TombstoneBox))
                    {
                        if(box is PrimeBox)//part-way through being copied to next table
                            CopySlotsAndCheck(table, idx);//make sure it’s there when we come to it.
                        else
                            copy.PutIfMatch(box, false, false);
                    }
                }
            }
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
            HashSet<T> hs = new HashSet<T>(_cmp);
            for(Table table = _table; table != null; table = table.Next)
            {
                Record[] records = table.Records;
                for(int idx = 0; idx != records.Length; ++idx)
                {
                    Box box = records[idx].Box;
                    if(box != null && !(box is TombstoneBox))
                    {
                        if(box is PrimeBox)//part-way through being copied to next table
                            CopySlotsAndCheck(table, idx);//make sure it’s there when we come to it.
                        else
                            hs.Add(box.Value);
                    }
                }
            }
            return hs;
        }
        /// <summary>Returns a <see cref="List&lt;T>"/> with the same contents as
        /// the lock-free set.</summary>
        /// <returns>The List.</returns>
        /// <remarks>Because this operation does not lock, the resulting set’s contents
        /// could be inconsistent in terms of an application’s use of the values, or include duplicate items</remarks>
        public List<T> ToList()
        {
            return new List<T>(ToHashSet());
        }
        /// <summary>Returns an array with the same contents as
        /// the lock-free set.</summary>
        /// <returns>The array.</returns>
        /// <remarks>Because this operation does not lock, the resulting set’s contents
        /// could be inconsistent in terms of an application’s use of the values, or include duplicate items</remarks>
        public T[] ToArray()
        {
            HashSet<T> hs = ToHashSet();
            T[] array = new T[hs.Count];
            hs.CopyTo(array);
            return array;
        }
        
        object ICollection.SyncRoot
        {
            get { throw new NotSupportedException(Strings.SyncRoot_Not_Supported); }
        }
        
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }
        
        bool IProducerConsumerCollection<T>.TryAdd(T item)
        {
            return Add(item);
        }
        /// <summary>Attempts to take a single item from the set.</summary>
        /// <param name="item">On return, the item removed, if successful.</param>
        /// <returns>True if an item was removed, false if the set had been empty.</returns>
        /// <remarks>The item returned is arbitrarily determined, with no guaranteed ordering.</remarks>
        public bool TryTake(out T item)
        {
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
                            CopySlotsAndCheck(table, idx);
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
                                    CopySlotsAndCheck(table, idx);
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
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
        	((ICollection)ToHashSet()).CopyTo(array, index);
        }
    }
    
    /// <summary>Provides further static methods for manipulating <see cref="ThreadSafeSet&lt;T>"/>’s with
    /// particular parameter types. In C♯ and VB.NET these extension methods can be called as instance methods on
    /// appropriately typed <see cref="ThreadSafeSet&lt;T>"/>s.</summary>
    /// <threadsafety static="true" instance="true"/>
    public static class SetExtensions
    {
        /// <summary>Retrieves a reference to the specified item.</summary>
        /// <typeparam name="T">The type of the items in the set.</typeparam>
        /// <param name="tset">The set to search.</param>
        /// <param name="item">The item sought.</param>
        /// <returns>A reference to a matching item if it is present in the set, null otherwise.</returns>
        /// <remarks>This allows use of the set to restrain a group of objects to exclude duplicates, allowing for reduced
        /// memory use, and reference-based equality checking, comparable with how <see cref="string.IsInterned(string)"/> allows
        /// one to check for a copy of a string in the CLR intern pool, but also allowing for removal, clearing and multiple pools. This is clearly
        /// only valid for reference types.</remarks>
        public static T Find<T>(this ThreadSafeSet<T> tset, T item) where T : class
        {
            T found;
            return tset.Obtain(item, out found) ? found : default(T);
        }
        /// <summary>Retrieves a reference to the specified item, adding it if necessary.</summary>
        /// <typeparam name="T">The type of the items in the set.</typeparam>
        /// <param name="tset">The set to search, and add to if necessary.</param>
        /// <param name="item">The item sought.</param>
        /// <returns>A reference to a matching item if it is present in the set, using the item given if there isn’t
        /// already a matching item.</returns>
        /// <exception cref="System.InvalidOperationException"> An attempt was made to use this when the generic type of the
        /// set is not a reference type (that is, a value or pointer type).</exception>
        /// <remarks>This allows use of the set to restrain a group of objects to exclude duplicates, allowing for reduced
        /// memory use, and reference-based equality checking, comparable with how <see cref="string.Intern(string)"/> allows
        /// one to check for a copy of a string in the CLR intern pool, but also allowing for removal, clearing and multiple pools. This is clearly
        /// only valid for reference types.</remarks>
        public static T FindOrStore<T>(this ThreadSafeSet<T> tset, T item) where T : class
        {
            ThreadSafeSet<T>.Box found = tset.PutIfMatch(new ThreadSafeSet<T>.Box(item), false, false);
            return found == null || found is ThreadSafeSet<T>.TombstoneBox ? item : found.Value;
        }
    }
}