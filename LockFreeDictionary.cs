using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Permissions;
using System.Threading;

namespace HackCraft.LockFree
{
    /// <summary>A dictionary which is thread-safe for all operations, without locking.
    /// </summary>
    /// <remarks>The documentation of <see cref="System.Collections.Generic.IDictionary&lt;TKey, TValue>"/> states
    /// that null keys may or may not be allowed by a conformant implentation. In this case, they are (for reference types).</remarks>
    [Serializable]
    public sealed class LockFreeDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICloneable, ISerializable, IDictionary
    {
        private const int REPROBE_LOWER_BOUND = 5;
        private const int REPROBE_SHIFT = 5;
        private const int ZERO_HASH = 0x55555555;
        
        internal class KV
        {
            public TKey Key;
            public TValue Value;
            public KV(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
            public void FillPrime(PrimeKV prime)
            {
            	prime.Key = Key;
            	prime.Value = Value;
            }
            public KV StripPrime()
            {
            	return this is PrimeKV ? new KV(Key, Value) : this;
            }
            public static readonly TombstoneKV DeadKey = new TombstoneKV(default(TKey));
            public static implicit operator KV(KeyValuePair<TKey, TValue> kvp)
            {
                return new KV(kvp.Key, kvp.Value);
            }
            public static implicit operator KeyValuePair<TKey, TValue>(KV kv)
            {
                return new KeyValuePair<TKey, TValue>(kv.Key, kv.Value);
            }
        }
        internal sealed class PrimeKV : KV
        {
        	public PrimeKV()
        	    :base(default(TKey), default(TValue)){}
        }
        internal sealed class TombstoneKV : KV
        {
        	public TombstoneKV(TKey key)
        		:base(key, default(TValue)){}
        }
        interface IKVEqualityComparer
        {
        	bool Equals(KV livePair, KV cmpPair);
        }
        private sealed class MatchesAll : IKVEqualityComparer
        {
        	private MatchesAll(){}
			public bool Equals(KV livePair, KV cmpPair)
			{
				return true;
			}
			public static readonly MatchesAll Instance = new MatchesAll();
        }
        private sealed class KVEqualityComparer : IKVEqualityComparer
        {
        	private readonly IEqualityComparer<TValue> _ecmp;
        	private KVEqualityComparer(IEqualityComparer<TValue> ecmp)
        	{
        		_ecmp = ecmp;
        	}
			public bool Equals(KV livePair, KV cmpPair)
			{
				if(livePair == null || livePair is TombstoneKV)
					return cmpPair == null || cmpPair is TombstoneKV;
				if(cmpPair == null || cmpPair is TombstoneKV)
					return false;
				return _ecmp.Equals(livePair.Value, cmpPair.Value);
			}
			private static IEqualityComparer<TValue> DefIEQ = EqualityComparer<TValue>.Default;
			public static KVEqualityComparer Default = new KVEqualityComparer(DefIEQ);
			public static KVEqualityComparer Create(IEqualityComparer<TValue> ieq)
			{
				if(ieq.Equals(DefIEQ))
					return Default;
				return new KVEqualityComparer(ieq);
			}
        }
        private sealed class NullPairEqualityComparer : IKVEqualityComparer
        {
        	private NullPairEqualityComparer(){}
			public bool Equals(KV livePair, KV cmpPair)
			{
				return livePair == null;
			}
			public static NullPairEqualityComparer Instance = new NullPairEqualityComparer();
        }
        [StructLayout(LayoutKind.Sequential, Pack=1)]
        private struct Record
        {
            public int Hash;
            public KV KeyValue;
        }
        private sealed class Table
        {
            public readonly Record[] Records;
            public volatile Table Next;
            public readonly AliasedInt Size;
            public readonly AliasedInt Slots = new AliasedInt();
            public readonly int Capacity;
            public readonly int Mask;
            public readonly int PrevSize;
            public readonly int ReprobeLimit;
            public int CopyIdx;
            public int Resizers;
            public int CopyDone;
            public Table(int capacity, AliasedInt size)
            {
                Records = new Record[Capacity = capacity];
                Mask = capacity - 1;
                ReprobeLimit = (capacity >> REPROBE_SHIFT) + REPROBE_LOWER_BOUND;
                if(ReprobeLimit > capacity)
                    ReprobeLimit = capacity;
                PrevSize = Size = size;
            }
        }
        
        private Table _table;
        private readonly int _initialCapacity;
        private readonly IEqualityComparer<TKey> _cmp;
        /// <summary>The capacity used with those constructors that do not take a capacity parameter.
        /// </summary>
        public static readonly int DefaultCapacity = 1;
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        /// <param name="capacity">The initial capactiy of the dictionary</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the keys.</param>
        public LockFreeDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
        	if(capacity < 0 || capacity > 0x4000000)
        		throw new ArgumentOutOfRangeException("capacity");
        	if(comparer == null)
        		throw new ArgumentNullException("comparer");
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
            	
            _table = new Table(_initialCapacity = capacity, new AliasedInt());
            _cmp = comparer;
        }
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        /// <param name="capacity">The initial capactiy of the dictionary</param>
        public LockFreeDictionary(int capacity)
            :this(capacity, EqualityComparer<TKey>.Default){}
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the keys.</param>
        public LockFreeDictionary(IEqualityComparer<TKey> comparer)
            :this(DefaultCapacity, comparer){}
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        public LockFreeDictionary()
            :this(DefaultCapacity){}
        private static int EstimateNecessaryCapacity(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
        	if(collection == null)
        		throw new ArgumentNullException("collection", "Cannot create a new lock-free dictionary from a null source collection");
        	ICollection<KeyValuePair<TKey, TValue>> colKVP = collection as ICollection<KeyValuePair<TKey, TValue>>;
        	if(colKVP != null)
        		return colKVP.Count;
        	ICollection col = collection as ICollection;
        	if(col != null)
        		return col.Count;
        	return DefaultCapacity;
        }
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        /// <param name="collection">A collection from which the dictionary will be filled.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;TKey>" /> that compares the keys.</param>
        public LockFreeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
        	:this(EstimateNecessaryCapacity(collection), comparer)
        {
        	foreach(KeyValuePair<TKey, TValue> kvp in collection)
        		this[kvp.Key] = kvp.Value;
        }
        /// <summary>Constructs a new LockFreeDictionary
        /// </summary>
        /// <param name="collection">A collection from which the dictionary will be filled.</param>
        public LockFreeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
            :this(collection, EqualityComparer<TKey>.Default){}
        private int Hash(TKey key)
        {
            //We must prohibit the value of zero in order to be sure that when we encounter a
            //zero, that the hash has not been written.
            //We do not use a Wang-Jenkins like Dr. Click's approach, since .NET's IComparer allows
            //users of the class to fix the effects of poor hash algorithms.
            int givenHash = _cmp.GetHashCode(key);
            return givenHash == 0 ? ZERO_HASH : givenHash;
        }
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter=true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("ic", _initialCapacity);
            info.AddValue("cmp", _cmp, typeof(IEqualityComparer<TKey>));
            int cItems = 0;
            foreach(KV pair in EnumerateKVs())
            {
                info.AddValue("k" + cItems, pair.Key, typeof(TKey));
                info.AddValue("v" + cItems, pair.Value, typeof(TValue));
                ++cItems;
            }
            info.AddValue("cKVP", cItems);
        }
        private LockFreeDictionary(SerializationInfo info, StreamingContext context)
            :this(info.GetInt32("cKVP"), (IEqualityComparer<TKey>)info.GetValue("cmp", typeof(IEqualityComparer<TKey>)))
        {
            _initialCapacity = info.GetInt32("ic");
            int count = info.GetInt32("cKVP");
            if(count < 0)
                throw new SerializationException();
            for(int i = 0; i != count; ++i)
                this[(TKey)info.GetValue("k" + i, typeof(TKey))] = (TValue)info.GetValue("v" + i, typeof(TValue));
        }
        private bool Obtain(TKey key, out TValue value)
        {
            return Obtain(_table, key, Hash(key), out value);
        }
        private bool Obtain(Table table, TKey key, int hash, out TValue value)
        {
            int idx = hash & table.Mask;
            int reprobeCount = 0;
            int maxProbe = table.ReprobeLimit;
            Record[] records = table.Records;
            for(;;)
            {
                int curHash = records[idx].Hash;
                if(curHash == 0)//nothing written to this record
                {
                    Table next = table.Next;
                    if(next != null)
                        return Obtain(next, key, hash, out value);
                    value = default(TValue);
                    return false;
                }
                KV pair = records[idx].KeyValue;
                if(curHash == hash)//hash we care about, is it the key we care about?
                {
                    if(_cmp.Equals(key, pair.Key))//key's match, and this can't change.
                    {
                    	PrimeKV asPrime = pair as PrimeKV;
                        if(asPrime != null)
                        {
                        	CopySlotsAndCheck(table, asPrime, idx);
                            return Obtain(table.Next, key, hash, out value);
                        }
                        else if(pair is TombstoneKV)
                        {
                            value = default(TValue);
                            return false;
                        }
                        else
                        {
                            value = pair.Value;
                            return true;
                        }
                    }
                }
                if(/*pair == KV.DeadKey ||*/ ++reprobeCount >= maxProbe)
                {
                    Table next = table.Next;
                    if(next == null)
                    {
                        value = default(TValue);
                        return false;
                    }
                    return Obtain(next, key, hash, out value);
                }
                idx = (idx + 1) & table.Mask;
            }
        }
        private static bool Equals(IEqualityComparer<TValue> valCmp, KV x, KV y)
        {
            if(x is TombstoneKV)
                return y is TombstoneKV;
            if(y is TombstoneKV)
                return false;
            return valCmp.Equals(x.Value, y.Value);
        }
        private KV PutIfMatch(KV pair, KV oldPair, IKVEqualityComparer valCmp)
        {
            return PutIfMatch(_table, pair, Hash(pair.Key), oldPair, valCmp);
        }
        private KV PutIfMatch(Table table, KV pair, int hash, KV oldPair, IKVEqualityComparer valCmp)
        {
            int mask = table.Mask;
            int idx = hash & mask;
            int reprobeCount = 0;
            int maxProbe = table.ReprobeLimit;
            Record[] records = table.Records;
            KV curPair;
            for(;;)
            {
                int curHash = records[idx].Hash;
                if(curHash == 0)//nothing written here
                {
                    if(pair is TombstoneKV)
                        return null;//don't change anything.
                    if((curHash = Interlocked.CompareExchange(ref records[idx].Hash, hash, 0)) == 0)
                        curHash = hash;
                    //now fallthrough to the next check, which we will pass if the above worked
                    //or if another thread happened to write the same hash we wanted to write
                }
                if(curHash == hash)
                {
                    //hashes match, do keys?
                    //while retrieving the current
                    //if we want to write to empty records
                    //let's see if we can just write because there's nothing there...
                    if(oldPair == KV.DeadKey || oldPair == null)
                    {
                        if((curPair = Interlocked.CompareExchange(ref records[idx].KeyValue, pair, null)) == null)
                        {
                            table.Slots.Increment();
                            if(oldPair != null && !(pair is TombstoneKV))
                                table.Size.Increment();
                            return null;
                        }
                    }
                    else
                        curPair = records[idx].KeyValue;
                    //okay there's something with the same hash here, does it have the same key?
                    if(_cmp.Equals(curPair.Key, pair.Key))
                        break;
                }
                else
                    curPair = records[idx].KeyValue; //just to check for dead records
                if(curPair == KV.DeadKey || ++reprobeCount >= maxProbe)
                {
                    Table next = table.Next ?? Resize(table);
                    //test if we're putting from a copy
                    //and don't do this if that's
                    //the case
                    PrimeKV prime = new PrimeKV();
                    HelpCopy(table, prime, false);
                    return PutIfMatch(next, pair, hash, oldPair, valCmp);
                }
                idx = (idx + 1) & mask;
            }
            //we have a record with a matching key.
            if(!typeof(TValue).IsValueType && !typeof(TValue).IsPointer && ReferenceEquals(pair.Value, curPair.Value))
                return pair;//short-cut on quickly-discovered no change.
            
            if(table.Next != null)
            {
                PrimeKV prime = new PrimeKV();
                CopySlotsAndCheck(table, prime, idx);
                HelpCopy(table, prime, false);
                return PutIfMatch(table.Next, pair, hash, oldPair, valCmp);
            }
            
            for(;;)
            {
            	if(!valCmp.Equals(curPair, oldPair))
                    return curPair;
                
                KV prevPair = Interlocked.CompareExchange(ref records[idx].KeyValue, pair, curPair);
                if(prevPair == curPair)
                {
                    if(oldPair != null)
                    {
                        if(pair is TombstoneKV)
                        {
                        	if(!(prevPair is TombstoneKV))
                                table.Size.Decrement();
                        }
                        else if(prevPair is TombstoneKV)
                            table.Size.Increment();
                    }
                    return prevPair;
                }
                
                //we lost the race, another thread set the pair.
                PrimeKV prevPrime = prevPair as PrimeKV;
                if(prevPrime != null)
                {
                    CopySlotsAndCheck(table, prevPrime, idx);
                    if(oldPair != null)
                        HelpCopy(table, prevPrime, false);
                    return PutIfMatch(table.Next, pair, hash, oldPair, valCmp);
                }
                else if(prevPair == KV.DeadKey)
                    return PutIfMatch(table.Next, pair, hash, oldPair, valCmp);
                else
                    curPair = prevPair;
            }
        }
        private void CopySlotsAndCheck(Table table, PrimeKV prime, int idx)
        {
            if(CopySlot(table, prime, idx))
                CopySlotAndPromote(table, 1);
        }
        private void HelpCopy(Table table, PrimeKV prime, bool all)
        {
            int chunk = table.Capacity;
            if(chunk > 1024)
                chunk = 1024;
            while(table.CopyDone < table.Capacity)
            {
                int copyIdx = Interlocked.Add(ref table.CopyIdx, chunk) & table.Mask;
                int workDone = 0;
                for(int i = 0; i != chunk; ++i)
                    if(CopySlot(table, prime, copyIdx + i))
                        ++workDone;
                if(workDone != 0)
                    CopySlotAndPromote(table, workDone);
                if(!all)
                    return;
            }
        }
        private void CopySlotAndPromote(Table table, int workDone)
        {
            if(Interlocked.Add(ref table.CopyDone, workDone) >= table.Capacity && table == _table)
                while(Interlocked.CompareExchange(ref _table, table.Next, table) == table)
                {
                    table = _table;
                    if(table.CopyDone < table.Capacity)
                        break;
                }
        }
        private bool CopySlot(Table table, PrimeKV prime, int idx)
        {
            Record[] records = table.Records;
            //if unwritten-to we should be able to just mark it as dead.
            if(records[idx].Hash == 0 && Interlocked.CompareExchange(ref records[idx].KeyValue, KV.DeadKey, null) == null)
                return true;
            KV kv = records[idx].KeyValue;
            KV oldKV = kv;
            while(!(kv is PrimeKV))
            {
            	if(kv is TombstoneKV)
            	{
            		oldKV = Interlocked.CompareExchange(ref records[idx].KeyValue, KV.DeadKey, kv);
            		if(oldKV == kv)
            			return true;
            	}
            	else
            	{
	            	kv.FillPrime(prime);
	                oldKV = Interlocked.CompareExchange(ref records[idx].KeyValue, prime, kv);
	                if(kv == oldKV)
	                {
	                    if(kv is TombstoneKV)
	                        return true;
	                    kv = prime;
	                    break;
	                }
            	}
                kv = oldKV;
            }
            if(kv is TombstoneKV)
                return false;

            KV newRecord = oldKV.StripPrime();
            
            bool copied = PutIfMatch(table.Next, newRecord, records[idx].Hash, null, NullPairEqualityComparer.Instance) == null;
            
            while((oldKV = Interlocked.CompareExchange(ref records[idx].KeyValue, KV.DeadKey, kv)) != kv)
                kv = oldKV;
            
            return copied;
        }
        private Table Resize(Table tab)
        {
            int sz = tab.Size;
            int cap = tab.Capacity;
            Table next = tab.Next;
            if(next != null)
                return next;
            int newCap;
            if(sz >= cap * 3 / 4)
                newCap = sz * 8;
            else if(sz >= cap / 2)
                newCap = sz * 4;
            else if(sz >= cap / 4)
                newCap = sz * 2;
            else
                newCap = sz;
         	if(tab.Slots >= sz << 1)
                newCap = cap << 1;
            if(newCap < cap)
                newCap = cap;
            if(sz == tab.PrevSize)
                newCap *= 2;

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
            
            int resizers = Interlocked.Increment(ref tab.Resizers);
            int MB = newCap / 0x40000;
            if(MB > 0 && resizers > 2)
            {
                if((next = tab.Next) != null)
                    return next;
                Thread.SpinWait(20);
                if((next = tab.Next) != null)
                    return next;
                Thread.Sleep(Math.Max(MB * 5 * resizers, 200));
            }
            
            if((next = tab.Next) != null)
                return next;
            
            next = new Table(newCap, tab.Size);

            #pragma warning disable 420 // CompareExchange has its own volatility guarantees
            return Interlocked.CompareExchange(ref tab.Next, next, null) ?? next;
			#pragma warning restore 420
        }
        /// <summary>
        /// The current capacity of the dictionary.
        /// </summary>
        /// <remarks>If the dictionary is in the midst of a resize, the capacity it is resizing to is returned, ignoring other internal storage in use.</remarks>
        public int Capacity
        {
        	get
        	{
        		return _table.Capacity;
        	}
        }
        private static readonly IEqualityComparer<TValue> DefaultValCmp = EqualityComparer<TValue>.Default;
        /// <summary>
        /// Creates an <see cref="System.Collections.Generic.IDictionary&lt;TKey, TValue>"/> that is
        /// a copy of the current contents.
        /// </summary>
        /// <remarks>Because this operation does not lock, the resulting dictionary’s contents
        /// could be inconsistent in terms of an application’s use of the values.
        /// <para>If there is a value stored with a null key, it is ignored.</para></remarks>
        /// <returns>The <see cref="System.Collections.Generic.IDictionary&lt;TKey, TValue>"/>.</returns>
    	public Dictionary<TKey, TValue> ToDictionary()
    	{
    		Dictionary<TKey, TValue> snapshot = new Dictionary<TKey, TValue>(Count, _cmp);
    		foreach(KV kv in EnumerateKVs())
    			if(kv.Key != null)
    				snapshot[kv.Key] = kv.Value;
    		return snapshot;
    	}
    	object ICloneable.Clone()
    	{
    	    return Clone();
    	}
    	/// <summary>
    	/// Returns a copy of the current dictionary.
    	/// </summary>
        /// <remarks>Because this operation does not lock, the resulting dictionary’s contents
        /// could be inconsistent in terms of an application’s use of the values.</remarks>
        /// <returns>The <see cref="LockFreeDictionary&lt;TKey, TValue>"/>.</returns>
        public LockFreeDictionary<TKey, TValue> Clone()
        {
        	LockFreeDictionary<TKey, TValue> snapshot = new LockFreeDictionary<TKey, TValue>(Count, _cmp);
        	foreach(KV kv in EnumerateKVs())
        		snapshot.PutIfMatch(kv, KV.DeadKey, MatchesAll.Instance);
        	return snapshot;
        }
        /// <summary>Gets or sets the value for a particular key.
        /// </summary>
        /// <exception cref="System.Collections.Generic.KeyNotFoundException">The key was not present in the dictionary.</exception>
        public TValue this[TKey key]
        {
            get
            {
                TValue ret;
                if(Obtain(key, out ret))
                    return ret;
                throw new KeyNotFoundException(key.ToString());
            }
            set
            {
            	PutIfMatch(new KV(key, value), KV.DeadKey, MatchesAll.Instance);
            }
        }
        /// <summary>Returns the collection of keys in the system.
        /// </summary>
        /// <remarks>This is a live collection, which changes with changes to the dictionary.</remarks>
        public KeyCollection Keys
        {
            get { return new KeyCollection(this); }
        }
        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
        	get { return Keys; }
        }
        /// <summary>Returns the collection of values in the system.
        /// </summary>
        /// <remarks>This is a live collection, which changes with changes to the dictionary.</remarks>
        public ValueCollection Values
        {
        	get { return new ValueCollection(this); }
        }
        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
        	get { return Values; }
        }
        /// <summary>Returns an estimate of the current number of items in the dictionary.
        /// </summary>
        public int Count
        {
            get { return _table.Size; }
        }
        
        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get { return false; }
        }
        bool IDictionary.IsReadOnly
        {
            get { return false; }
        }
        /// <summary>Tests whether a given key is present in the collection
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            TValue dummy;
            return Obtain(key, out dummy);
        }
        /// <summary>Adds a key and value to the collection, as long as it is not currently present.
        /// </summary>
        /// <param name="key">The key to add.</param>
        /// <param name="value">The value to add.</param>
        /// <exception cref="System.ArgumentException">An item with the same key has already been added.</exception>
        public void Add(TKey key, TValue value)
        {
            KV ret = PutIfMatch(new KV(key, value), KV.DeadKey, KVEqualityComparer.Default);
            if(ret != null && !(ret is TombstoneKV))
                throw new ArgumentException("An item with the same key has already been added.", "key");
        }
        /// <summary>Attempts to remove an item from the collection, identified by its key.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the item was removed, false if it wasn't found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(TKey key)
        {
        	KV ret = PutIfMatch(new TombstoneKV(key), KV.DeadKey, MatchesAll.Instance);
        	return ret != null && !(ret is TombstoneKV);
        }
        /// <summary>Attempts to retrieve the value associated with a key.
        /// </summary>
        /// <param name="key">The key searched for.</param>
        /// <param name="value">The value found (if successful).</param>
        /// <returns>True if the key was found, false otherwise.</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return Obtain(key, out value);
        }
        /// <summary>Adds a key and value to the collection, as long as it is not currently present.
        /// </summary>
        /// <param name="item">The key and value to add.</param>
        /// <exception cref="System.ArgumentException">An item with the same key has already been added.</exception>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }
        /// <summary>
        /// Removes all items from the dictionary.
        /// </summary>
        /// <remarks>All items are removed in a single atomic operation.</remarks>
        public void Clear()
        {
            Thread.MemoryBarrier();
            _table = new Table(_initialCapacity, new AliasedInt());
        }
        /// <summary>
        /// Tests whether a key and value matching that passed are present in the dictionary
        /// </summary>
        /// <param name="item">A <see cref="System.Collections.Generic.KeyValuePair&lt;TKey,TValue>"/> defining the item sought.</param>
        /// <param name="valueComparer">An <see cref="System.Collections.Generic.IEqualityComparer&lt;T>"/> used to test a value found
        /// with that sought.</param>
        /// <returns>True if the key and value are found, false otherwise.</returns>
        public bool Contains(KeyValuePair<TKey, TValue> item, IEqualityComparer<TValue> valueComparer)
        {
            TValue test;
            return Obtain(item.Key, out test) && valueComparer.Equals(item.Value, test);
        }
        /// <summary>
        /// Tests whether a key and value matching that passed are present in the dictionary
        /// </summary>
        /// <param name="item">A <see cref="System.Collections.Generic.KeyValuePair&lt;TKey,TValue>"/> defining the item sought.</param>
        /// <returns>True if the key and value are found, false otherwise.</returns>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return Contains(item, EqualityComparer<TValue>.Default);
        }
        /// <summary>
        /// Copies the contents of the dictionary to an array.
        /// </summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array to start copying from</param>
        /// <exception cref="System.ArgumentNullException"/>The array was null.
        /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
        /// <exception cref="System.ArgumentException"/>The number of items in the collection was
        /// too great to copy into the array at the index given.
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(arrayIndex < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
        	Dictionary<TKey, TValue> snapshot = ToDictionary();
        	TValue valForNull;
        	if(!typeof(TKey).IsValueType && !typeof(TKey).IsPointer && TryGetValue(default(TKey), out valForNull))
        	{
	        	if(arrayIndex + snapshot.Count + 1 > array.Length)
	        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
	        	array[arrayIndex++] = new KeyValuePair<TKey, TValue>(default(TKey), valForNull);
        	}
        	((ICollection<KeyValuePair<TKey, TValue>>)snapshot).CopyTo(array, arrayIndex);
        }
        /// <summary>Removes an item from the collection.
        /// </summary>
        /// <param name="item">The item to remove</param>
        /// <param name="valueComparer">A <see cref="System.Collections.Generic.IEqualityComparer&lt;T>"/> that is used in considering whether
        /// an item found is equal to that searched for.</param>
        /// <param name="removed">The item removed (if successful).</param>
        /// <returns>True if the item was removed, false if no matching item was found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(KeyValuePair<TKey, TValue> item, IEqualityComparer<TValue> valueComparer, out KeyValuePair<TKey, TValue> removed)
        {
        	KV rem = PutIfMatch(new TombstoneKV(item.Key), item, KVEqualityComparer.Create(valueComparer));
        	if(rem == null || rem is TombstoneKV || !valueComparer.Equals(rem.Value, item.Value))
        	{
        		removed = default(KeyValuePair<TKey, TValue>);
        		return false;
        	}
        	removed = rem;
        	return true;
        }
        /// <summary>Removes an item from the collection.
        /// </summary>
        /// <param name="item">The item to remove</param>
        /// <param name="valueComparer">An <see cref="System.Collections.Generic.IEqualityComparer&lt;T>"/> used to test a value found</param>
        /// <returns>True if the item was removed, false if no matching item was found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(KeyValuePair<TKey, TValue> item, IEqualityComparer<TValue> valueComparer)
        {
            KeyValuePair<TKey, TValue> dummy;
            return Remove(item, valueComparer, out dummy);
        }
        /// <summary>Removes an item from the collection.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="cmpValue">The value to remove.</param>
        /// <param name="valueComparer">A <see cref="System.Collections.Generic.IEqualityComparer&lt;T>"/> that is used in considering whether
        /// an item found is equal to that searched for.</param>
        /// <param name="removed">The item removed (if successful).</param>
        /// <returns>True if the item was removed, false if no matching item was found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(TKey key, TValue cmpValue, IEqualityComparer<TValue> valueComparer, out TValue removed)
        {
        	KV rem = PutIfMatch(new TombstoneKV(key), new KV(key, cmpValue), KVEqualityComparer.Create(valueComparer));
        	if(rem == null || rem is TombstoneKV || !valueComparer.Equals(cmpValue, rem.Value))
        	{
        		removed = default(TValue);
        		return false;
        	}
        	removed = rem.Value;
        	return true;
        }
        /// <summary>Removes an item from the collection.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="cmpValue">The value to remove.</param>
        /// <param name="removed">The item removed (if successful).</param>
        /// <returns>True if the item was removed, false if no matching item was found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(TKey key, TValue cmpValue, out TValue removed)
        {
        	return Remove(key, cmpValue, DefaultValCmp, out removed);
        }
        /// <summary>Removes a <see cref="System.Collections.Generic.KeyValuePair&lt;TKey,TValue>"/> from the collection.
        /// </summary>
        /// <param name="item">The item to remove</param>
        /// <returns>True if the item was removed, false if no matching item was found.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item, DefaultValCmp);
        }
        /// <summary>Removes items from the dictionary that match a predicate.
        /// </summary>
        /// <param name="predicate">A <see cref="System.Func&lt;T1, T2, TResult>"/> that returns true for the items that should be removed.</param>
        /// <returns>A <see cref="System.Collections.Generic.IEnumerable&lt;T>"/> of the items removed.</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.
        /// <para>The returned enumerable is lazily executed, and items are only removed from the dictionary as it is enumerated.</para></remarks>
        public IEnumerable<KeyValuePair<TKey, TValue>> RemoveWhere(Func<TKey, TValue, bool> predicate)
        {
        	int removed;
        	Table table = _table;
        	for(;;)
        	{
	        	removed = 0;
	            Record[] records = table.Records;
	            for(int idx = 0; idx != records.Length; ++idx)
	            {
	            	Record record = records[idx];
	            	KV pair = record.KeyValue;
	            	PrimeKV prime = pair as PrimeKV;
	            	if(prime != null)
	            		CopySlotsAndCheck(table, prime, idx);
	            	else if(pair != null && !(pair is TombstoneKV) && predicate(pair.Key, pair.Value))
	            	{
	            		TombstoneKV tomb = new TombstoneKV(pair.Key);
	            		for(;;)
	            		{
		            		KV oldPair = Interlocked.CompareExchange(ref records[idx].KeyValue, tomb, pair);
		            		if(oldPair == pair)
		            		{
		            			table.Size.Decrement();
		            			yield return pair;
		            			++removed;
		            			break;
		            		}
		            		else if(oldPair is PrimeKV)
		            		    CopySlotsAndCheck(table, (PrimeKV)oldPair, idx);
		            		else if(oldPair is TombstoneKV || !predicate(oldPair.Key, oldPair.Value))
		            			break;
		            		else
		            			pair = oldPair;
	            		}
	            	}
	            }
	            Table next = table.Next;
	            if(next != null)
	            	table = next;
	            else
	            {
	            	if(removed > table.Capacity >> 4 || removed > Count >> 2)
	            		Resize(table);
	            	yield break;
	            }
        	}
        }
        /// <summary>Removes all key-value pairs that match a predicate.
        /// </summary>
        /// <param name="predicate">A <see cref="System.Func&lt;T1, T2, TResult>"/> that returns true when passed a key and value that should be removed.</param>
        /// <returns>The number of items removed</returns>
        /// <remarks>Removal internally requires an allocation. This is generally negliable, but it should be noted
        /// that <see cref="System.OutOfMemoryException"/> exceptions are possible in memory-critical situations.</remarks>
        public int Remove(Func<TKey, TValue, bool> predicate)
        {
        	int total = 0;
        	foreach(KeyValuePair<TKey, TValue> kvp in RemoveWhere(predicate))
        		++total;
        	return total;
        }
        internal sealed class KVEnumerator : IEnumerator<KV>, IEnumerable<KV>
        {
        	private readonly LockFreeDictionary<TKey, TValue> _dict;
            private Table _tab;
            private KV _current;
            private int _idx = -1;
            public KVEnumerator(LockFreeDictionary<TKey, TValue> dict)
            {
            	_tab = (_dict = dict)._table;
            }
            public KV Current
            {
                get
                {
                    return _current;
                }
            }
            object IEnumerator.Current
            {
                get { return Current; }
            }
            public void Dispose(){}
            public bool MoveNext()
            {
                for(;_tab != null; _tab = _tab.Next, _idx = -1)
                {
                    Record[] records = _tab.Records;
                    for(++_idx; _idx != records.Length; ++_idx)
                    {
                        KV kv = records[_idx].KeyValue;
                        if(kv != null && !(kv is TombstoneKV))
                        {
                        	PrimeKV prime = kv as PrimeKV;
                        	if(prime != null)//part-way through being copied to next table
                        		_dict.CopySlotsAndCheck(_tab, prime, _idx);//make sure it's there when we come to it.
                        	else
                        	{
	                            _current = kv;
	                            return true;
                        	}
                        }
                    }
                }
                return false;
            }
            public void Reset()
            {
            	_tab = _dict._table;
            	_idx = -1;
            }
            public KVEnumerator GetEnumerator()
            {
                return this;
            }
            
            IEnumerator<KV> IEnumerable<KV>.GetEnumerator()
            {
                return this;
            }
            
            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }
        }
        private KVEnumerator EnumerateKVs()
        {
            return new KVEnumerator(this);
        }
        /// <summary>Enumerates a LockFreeDictionary&lt;TKey, TValue>.
        /// </summary>
        /// <remarks>The use of a value type for <see cref="System.Collections.Generic.List&lt;T>.Enumerator"/> has drawn some criticism.
        /// Note that this does not apply here, as the state that changes with enumeration is not maintained by the structure itself.</remarks>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private KVEnumerator _src;
            internal Enumerator(KVEnumerator src)
            {
                _src = src;
            }
            /// <summary>
            /// Returns the current <see cref="System.Collections.Generic.KeyValuePair&lt;TKey,TValue>"/> being enumerated.
            /// </summary>
            public KeyValuePair<TKey, TValue> Current
            {
                get
                {
                    return _src.Current;
                }
            }
            object IEnumerator.Current
            {
                get
                {
                    return Current;
                }
            }
            void IDisposable.Dispose()
            {
                _src.Dispose();
            }
            /// <summary>
            /// Moves to the next item in the enumeration.
            /// </summary>
            /// <returns>True if another item was found, false if the end of the enumeration was reached.</returns>
            public bool MoveNext()
            {
                return _src.MoveNext();
            }
            /// <summary>
            /// Reset the enumeration
            /// </summary>
            public void Reset()
            {
                _src.Reset();
            }
            object IDictionaryEnumerator.Key
            {
                get { return Current.Key; }
            }
            object IDictionaryEnumerator.Value
            {
                get { return Current.Value; }
            }
            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get { return new DictionaryEntry(Current.Key, Current.Value); }
            }
        }
        /// <summary>Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(EnumerateKVs());
        }
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        /// <summary>A collection of the values in a LockFreeDictionary.</summary>
        /// <remarks>The collection is "live" and immediately reflects changes in the dictionary.</remarks>
	    public struct ValueCollection : ICollection<TValue>, ICollection
	    {
	    	private readonly LockFreeDictionary<TKey, TValue> _dict;
	    	internal ValueCollection(LockFreeDictionary<TKey, TValue> dict)
	    	{
	    		_dict = dict;
	    	}
	    	/// <summary>
	    	/// The number of items in the collection.
	    	/// </summary>
			public int Count
			{
				get { return _dict.Count; }
			}
			bool ICollection<TValue>.IsReadOnly
			{
				get { return true; }
			}
			/// <summary>
			/// Tests the collection for the presence of an item.
			/// </summary>
			/// <param name="item">The item to search for.</param>
			/// <param name="cmp">An <see cref="System.Collections.Generic.IEqualityComparer&lt;T>"/> to use in comparing
			/// items found with that sought.</param>
			/// <returns>True if a matching item  was found, false otherwise.</returns>
			public bool Contains(TValue item, IEqualityComparer<TValue> cmp)
			{
				foreach(TValue val in this)
					if(cmp.Equals(item, val))
						return true;
				return false;
			}
			/// <summary>
			/// Tests the collection for the presence of an item.
			/// </summary>
			/// <param name="item">The item to search for.</param>
			/// <returns>True if a matching item  was found, false otherwise.</returns>
			public bool Contains(TValue item)
			{
				return Contains(item, DefaultValCmp);
			}
            /// <summary>
            /// Copies the contents of the collection to an array.
            /// </summary>
            /// <param name="array">The array to copy to.</param>
            /// <param name="arrayIndex">The index within the array to start copying from</param>
            /// <exception cref="System.ArgumentNullException"/>The array was null.
            /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
            /// <exception cref="System.ArgumentException"/>The number of items in the collection was
            /// too great to copy into the array at the index given.
			public void CopyTo(TValue[] array, int arrayIndex)
			{
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(arrayIndex < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	Dictionary<TKey, TValue> snapshot = _dict.ToDictionary();
	        	TValue valForNull;
	        	if(!typeof(TKey).IsValueType && !typeof(TKey).IsPointer && _dict.TryGetValue(default(TKey), out valForNull))
	        	{
		        	if(arrayIndex + snapshot.Count + 1 > array.Length)
		        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
		        	array[arrayIndex++] = valForNull;
	        	}
	        	snapshot.Values.CopyTo(array, arrayIndex);
			}
			/// <summary>Enumerates a value collection.
			/// </summary>
            /// <remarks>The use of a value type for <see cref="System.Collections.Generic.List&lt;T>.Enumerator"/> has drawn some criticism.
            /// Note that this does not apply here, as the state that changes with enumeration is not maintained by the structure itself.</remarks>
			public struct Enumerator : IEnumerator<TValue>
			{
	            private KVEnumerator _src;
	            internal Enumerator(KVEnumerator src)
	            {
	                _src = src;
	            }
	            /// <summary>
	            /// Returns the current value being enumerated.
	            /// </summary>
	            public TValue Current
	            {
	                get
	                {
	                    return _src.Current.Value;
	                }
	            }
	            object IEnumerator.Current
	            {
	                get
	                {
	                    return Current;
	                }
	            }
	            void IDisposable.Dispose()
	            {
	                _src.Dispose();
	            }
	            /// <summary>
	            /// Moves to the next item being iterated.
	            /// </summary>
	            /// <returns>True if another item is found, false if the end of the collection is reached.</returns>
	            public bool MoveNext()
	            {
	                return _src.MoveNext();
	            }
	            /// <summary>
	            /// Reset the enumeration
	            /// </summary>
	            public void Reset()
	            {
	                _src.Reset();
	            }
			}
			/// <summary>
			/// Returns an enumerator that iterates through the collection.
			/// </summary>
			/// <returns>The <see cref="Enumerator"/> that performs the iteration.</returns>
			public Enumerator GetEnumerator()
			{
				return new Enumerator(_dict.EnumerateKVs());
			}
			IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
			{
			    return GetEnumerator();
			}
			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
			void ICollection<TValue>.Add(TValue item)
			{
				throw new NotSupportedException();
			}
			void ICollection<TValue>.Clear()
			{
				throw new NotSupportedException();
			}
			bool ICollection<TValue>.Remove(TValue item)
			{
				throw new NotSupportedException();
			}
            object ICollection.SyncRoot
            {
                get { throw new NotSupportedException("SyncRoot property is not supported, and unnecesary with this class."); }
            }	        
            bool ICollection.IsSynchronized
            {
                get { return false; }
            }
            void ICollection.CopyTo(Array array, int index)
            {
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(index < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	((ICollection)_dict.ToDictionary().Values).CopyTo(array, index);
            }
	    }
        /// <summary>A collection of the keys in a LockFreeDictionary.</summary>
        /// <remarks>The collection is "live" and immediately reflects changes in the dictionary.</remarks>
	    public struct KeyCollection : ICollection<TKey>, ICollection
	    {
	    	private readonly LockFreeDictionary<TKey, TValue> _dict;
	    	internal KeyCollection(LockFreeDictionary<TKey, TValue> dict)
	    	{
	    		_dict = dict;
	    	}
	    	/// <summary>
	    	/// The number of items in the collection.
	    	/// </summary>
			public int Count
			{
				get { return _dict.Count; }
			}
			bool ICollection<TKey>.IsReadOnly
			{
				get { return true; }
			}
			/// <summary>
			/// Tests for the presence of a key in the collection.
			/// </summary>
			/// <param name="item">The key to search for.</param>
			/// <returns>True if the key is found, false otherwise.</returns>
			public bool Contains(TKey item)
			{
				return _dict.ContainsKey(item);
			}
            /// <summary>
            /// Copies the contents of the dictionary to an array.
            /// </summary>
            /// <param name="array">The array to copy to.</param>
            /// <param name="arrayIndex">The index within the array to start copying from</param>
            /// <exception cref="System.ArgumentNullException"/>The array was null.
            /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
            /// <exception cref="System.ArgumentException"/>The number of items in the collection was
            /// too great to copy into the array at the index given.
			public void CopyTo(TKey[] array, int arrayIndex)
			{
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(arrayIndex < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	Dictionary<TKey, TValue> snapshot = _dict.ToDictionary();
	        	if(!typeof(TKey).IsValueType && !typeof(TKey).IsPointer && _dict.ContainsKey(default(TKey)))
	        	{
		        	if(arrayIndex + snapshot.Count + 1 > array.Length)
		        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
	        		array[arrayIndex++] = default(TKey);
	        	}
	        	snapshot.Keys.CopyTo(array, arrayIndex);
			}
			/// <summary>Enumerates a key collection
			/// </summary>
            /// <remarks>The use of a value type for <see cref="System.Collections.Generic.List&lt;T>.Enumerator"/> has drawn some criticism.
            /// Note that this does not apply here, as the state that changes with enumeration is not maintained by the structure itself.</remarks>
			public struct Enumerator : IEnumerator<TKey>
			{
	            private KVEnumerator _src;
	            internal Enumerator(KVEnumerator src)
	            {
	                _src = src;
	            }
	            /// <summary>
	            /// Returns the current item being enumerated.
	            /// </summary>
	            public TKey Current
	            {
	                get
	                {
	                    return _src.Current.Key;
	                }
	            }
	            object IEnumerator.Current
	            {
	                get
	                {
	                    return Current;
	                }
	            }
	            void IDisposable.Dispose()
	            {
	                _src.Dispose();
	            }
                /// <summary>
                /// Moves to the next item in the enumeration.
                /// </summary>
                /// <returns>True if another item was found, false if the end of the enumeration was reached.</returns>
	            public bool MoveNext()
	            {
	                return _src.MoveNext();
	            }
	            /// <summary>
	            /// Reset the enumeration
	            /// </summary>
	            public void Reset()
	            {
	                _src.Reset();
	            }
			}
            /// <summary>Returns an enumerator that iterates through the collection.
            /// </summary>
            /// <returns>The enumerator.</returns>
			public Enumerator GetEnumerator()
			{
				return new Enumerator(_dict.EnumerateKVs());
			}
			IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator()
			{
			    return GetEnumerator();
			}
			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
			void ICollection<TKey>.Add(TKey item)
			{
				throw new NotSupportedException();
			}
			void ICollection<TKey>.Clear()
			{
				throw new NotSupportedException();
			}
			bool ICollection<TKey>.Remove(TKey item)
			{
				throw new NotSupportedException();
			}
            object ICollection.SyncRoot
            {
                get { throw new NotSupportedException("SyncRoot property is not supported, and unnecesary with this class."); }
            }
	        
            bool ICollection.IsSynchronized
            {
                get { return false; }
            }
            void ICollection.CopyTo(Array array, int index)
            {
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(index < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	Dictionary<TKey, TValue> snapshot = _dict.ToDictionary();
	        	((ICollection)_dict.ToDictionary().Keys).CopyTo(array, index);
            }
	    }
	    object IDictionary.this[object key]
        {
            get
            {
                TValue ret;
                if(key == null && (typeof(TKey).IsValueType || typeof(TKey).IsPointer))
                    return null;
                if(key is TKey || key == null)
                    return TryGetValue((TKey)key, out ret) ? (object)ret : null;
                return null;
            }
            set
            {
                if(key == null && (typeof(TKey).IsValueType || typeof(TKey).IsPointer))
                    throw new ArgumentException("Null (nothing) values cannot be cast to " + typeof(TKey).FullName + ".", "key");
                if(value == null && (typeof(TValue).IsValueType || typeof(TValue).IsPointer))
                    throw new ArgumentException("Null (nothing) values cannot be cast to " + typeof(TValue).FullName + ".", "value");
                try
                {
                    TKey convKey = (TKey)key;
                    try
                    {
                        this[convKey] = (TValue)value;
                    }
                    catch(InvalidCastException)
                    {
                        throw new ArgumentException("Cannot use " + key.GetType().FullName + " arguments as " + typeof(TValue).FullName + " values.", "value");
                    }
                }
                catch(InvalidCastException)
                {
                    throw new ArgumentException("Cannot use " + key.GetType().FullName + " arguments as " + typeof(TKey).FullName + " keys.", "key");
                }
            }
        }
        
        ICollection IDictionary.Keys
        {
            get { return Keys; }
        }
        
        ICollection IDictionary.Values
        {
            get { return Values; }
        }
        
        bool IDictionary.IsFixedSize
        {
            get { return false; }
        }
        
        object ICollection.SyncRoot
        {
            get { throw new NotSupportedException("SyncRoot property is not supported, and unnecesary with this class."); }
        }
        
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }
        
        bool IDictionary.Contains(object key)
        {
            if(key == null)
                return !typeof(TKey).IsValueType && !typeof(TKey).IsPointer && ContainsKey(default(TKey));
            return key is TKey && ContainsKey((TKey)key);
        }
        
        void IDictionary.Add(object key, object value)
        {
            if(key == null && (typeof(TKey).IsValueType || typeof(TKey).IsPointer))
                throw new ArgumentException("Null (nothing) values cannot be cast to " + typeof(TKey).FullName + ".", "key");
            if(value == null && (typeof(TValue).IsValueType || typeof(TValue).IsPointer))
                throw new ArgumentException("Null (nothing) values cannot be cast to " + typeof(TValue).FullName + ".", "value");
            try
            {
                TKey convKey = (TKey)key;
                try
                {
                    Add(convKey, (TValue)value);
                }
                catch(InvalidCastException)
                {
                    throw new ArgumentException("Cannot use " + key.GetType().FullName + " arguments as " + typeof(TValue).FullName + " values.", "value");
                }
            }
            catch(InvalidCastException)
            {
                throw new ArgumentException("Cannot use " + key.GetType().FullName + " arguments as " + typeof(TKey).FullName + " keys.", "key");
            }
        }
        
        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return GetEnumerator();
        }
        void IDictionary.Remove(object key)
        {
            if(key == null && (typeof(TKey).IsValueType || typeof(TKey).IsPointer))
                return;
            if(key == null || key is TKey)
                Remove((TKey)key);
        }
        
        void ICollection.CopyTo(Array array, int index)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(array.Rank != 1)
        	    throw new ArgumentException("Cannot copy to a multi-dimensional array", "array");
        	if(array.GetLowerBound(0) != 0)
        	    throw new ArgumentException("Cannot copy to an array whose lower bound is not zero", "array");
        	if(index < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
        	((ICollection)ToDictionary()).CopyTo(array, index);
        }
    }
}