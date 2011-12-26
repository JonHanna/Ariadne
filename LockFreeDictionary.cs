using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace HackCraft.LockFree
{
    public sealed class LockFreeDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private const int REPROBE_LOWER_BOUND = 5;
        private const int REPROBE_SHIFT = 5;
        private const int ZERO_HASH = 0x55555555;
        
        private static readonly bool ReferenceEqualsBoxes = typeof(TValue).IsValueType;
        
        internal sealed class RefInt
        {
            private int _value;
            public static implicit operator int(RefInt ri)
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
/*            public KV Prime()
            {
                return (State & Primed) != 0 ? this : new KV(Key, Value, State | Primed);
            }
            public KV StripPrime()
            {
                return (State & Primed) == 0 ? this : new KV(Key, Value, State & ~Primed);
            }
            public static KV TombstoneVersion(TKey key)
            {
                return new KV(key, default(TValue), Tombstone);
            }
            public bool IsPrime
            {
                get { return (State & Primed) != 0; }
            }
            public bool IsTombstone
            {
                get { return (State & Tombstone) != 0; }
            }*/
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
        	public PrimeKV(TKey key, TValue value)
        		:base(key, value){}
        }
        internal sealed class TombstoneKV : KV
        {
        	public TombstoneKV(TKey key)
        		:base(key, default(TValue)){}
        }
        internal interface IKVEqualityComparer
        {
        	bool Equals(KV livePair, KV cmpPair);
        }
        internal class MatchesAll : IKVEqualityComparer
        {
        	private MatchesAll(){}
			public bool Equals(KV livePair, KV cmpPair)
			{
				return true;
			}
			public static readonly MatchesAll Instance = new MatchesAll();
        }
        internal class KVEqualityComparer : IKVEqualityComparer
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
        internal class NullPairEqualityComparer : IKVEqualityComparer
        {
        	private NullPairEqualityComparer(){}
			public bool Equals(KV livePair, KV cmpPair)
			{
				return livePair == null;
			}
			public static NullPairEqualityComparer Instance = new NullPairEqualityComparer();
        }
        [StructLayout(LayoutKind.Sequential, Pack=1)]
        internal struct Record
        {
            public int Hash;
            public KV KeyValue;
        }
        internal sealed class Table
        {
            public readonly Record[] Records;
            public volatile Table Next;
            public readonly RefInt Size;
            public readonly RefInt Slots = new RefInt();
            public readonly int Capacity;
            public readonly int Mask;
            public readonly int PrevSize;
            public readonly int ReprobeLimit;
            public int CopyIdx;
            public int Resizers;
            public int CopyDone;
            public Table(int capacity, RefInt size)
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
        public static readonly int DefaultCapacity = 1;
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
            	
            _table = new Table(_initialCapacity = capacity, new RefInt());
            _cmp = comparer;
        }
        public LockFreeDictionary(int capacity)
            :this(capacity, EqualityComparer<TKey>.Default){}
        public LockFreeDictionary(IEqualityComparer<TKey> comparer)
            :this(DefaultCapacity, comparer){}
        public LockFreeDictionary()
            :this(DefaultCapacity){}
        private static int EstimateCount(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        {
        	if(collection == null)
        		throw new ArgumentNullException("collection", "Cannot create a new lock-free dictionary from a null source collection");
        	ICollection<KeyValuePair<TKey, TValue>> colKVP = collection as ICollection<KeyValuePair<TKey, TValue>>;
        	if(colKVP != null)
        		return colKVP.Count;
        	ICollection col = collection as ICollection;
        	if(col != null)
        		return col.Count;
        	return 1;
        }
        public LockFreeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
        	:this(EstimateCount(collection), comparer)
        {
        	foreach(KeyValuePair<TKey, TValue> kvp in collection)
        		this[kvp.Key] = kvp.Value;
        }
        public LockFreeDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
            :this(collection, EqualityComparer<TKey>.Default){}
        private int Hash(TKey key)
        {
            int givenHash = _cmp.GetHashCode(key);
            return givenHash == 0 ? ZERO_HASH : givenHash;
        }
        private bool Obtain(TKey key, out TValue value)
        {
            return Obtain(_table, key, Hash(key), out value);
        }
        private bool Obtain(Table table, TKey key, int hash, out TValue value)
        {
            int startIdx = hash & table.Mask;
            int idx = startIdx;
            int reprobeCount = 0;
            int maxProbe = table.ReprobeLimit;
            Record[] records = table.Records;
            for(;;)
            {
                int curHash = records[idx].Hash;
                if(curHash == 0)//nothing written to this record
                {
                    value = default(TValue);
                    return false;
                }
                KV pair = null;
                if(curHash == hash)//hash we care about, is it the key we care about?
                {
                    pair = records[idx].KeyValue;
                    if(_cmp.Equals(key, pair.Key))//key's match, and this can't change.
                    {
                    	PrimeKV asPrev = pair as PrimeKV;
                        if(asPrev != null)
                        {
                        	CopySlotsAndCheck(table, asPrev, idx);
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
                else
                {
                	pair = records[idx].KeyValue;
                }
                if(/*pair == KV.DeadKey ||*/ ++reprobeCount >= maxProbe)
                {
                    if(table.Next == null)
                    {
                        value = default(TValue);
                        return false;
                    }
                    return Obtain(table.Next, key, hash, out value);
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
                    {
                        curPair = records[idx].KeyValue;
                    }
                    //okay there's something with the same hash here, does it have the same key?
                    if(_cmp.Equals(curPair.Key, pair.Key))
                        break;
                }
                else
                {
                    curPair = records[idx].KeyValue; //just to check for dead records
                }
                if(curPair == KV.DeadKey || ++reprobeCount >= maxProbe)
                {
                    Table next = table.Next ?? Resize(table);
                    //test if we're putting from a copy
                    //and don't do this if that's
                    //the case
                    PrimeKV prevPrime = curPair as PrimeKV ?? new PrimeKV(curPair.Key, curPair.Value);
                    HelpCopy(table, prevPrime, false);
                    return PutIfMatch(next, pair, hash, oldPair, valCmp);
                }
                idx = (idx + 1) & mask;
            }
            //we have a record with a matching key.
            if(!ReferenceEqualsBoxes && ReferenceEquals(pair.Value, curPair.Value))
                return pair;//short-cut on quickly-discovered no change.
            
            //if(table.Next == null && reprobeCount >= REPROBE_LOWER_BOUND && table.Slots >= maxProbe)
              //  Resize(table, false);
            
            if(table.Next != null)
            {
            	PrimeKV prevPrime = curPair as PrimeKV ?? new PrimeKV(curPair.Key, curPair.Value);
                CopySlotsAndCheck(table, prevPrime, idx);
                HelpCopy(table, prevPrime, false);
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
            if(Interlocked.Add(ref table.CopyDone, workDone) >= table.Capacity)
            {
                //Debug.Assert(table.CopyDone == table.Capacity);
                if(table != _table)
                    return;
                for(;;)
                {
                    table = _table;
                    if(table.CopyDone < table.Capacity || Interlocked.CompareExchange(ref _table, table.Next, table) != table)
                        return;
                }
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
        private Table Resize(Table tab, bool secondTry = false)
        {
            int sz = tab.Size;
            int cap = tab.Capacity;
            Table next = tab.Next;
            if(next != null)
                return next;
            int newCap;
            if(secondTry)
                newCap = sz;
            {
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
                if(!secondTry)
                {
                    if(newCap < cap)
                        newCap = cap;
                    if(sz == tab.PrevSize)
                        newCap *= 2;
                }
            }

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
        public int Capacity
        {
        	get
        	{
        		return _table.Capacity;
        	}
        }
        private static readonly IEqualityComparer<TValue> DefaultValCmp = EqualityComparer<TValue>.Default;
    	public Dictionary<TKey, TValue> SnapshotDictionary()
    	{
    		Dictionary<TKey, TValue> snapshot = new Dictionary<TKey, TValue>(Count, _cmp);
    		foreach(KV kv in EnumerateKVs())
    			if(kv.Key != null)
    				snapshot[kv.Key] = kv.Value;
    		return snapshot;
    	}
        public LockFreeDictionary<TKey, TValue> Snapshot()
        {
        	LockFreeDictionary<TKey, TValue> snapshot = new LockFreeDictionary<TKey, TValue>(Count, _cmp);
        	foreach(KV kv in EnumerateKVs())
        	{
        		snapshot.PutIfMatch(kv, KV.DeadKey, MatchesAll.Instance);
        	}
        	return snapshot;
        }
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
        
        public KeyCollection Keys
        {
            get { return new KeyCollection(this); }
        }
        
        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
        	get { return Keys; }
        }
        
        public ValueCollection Values
        {
        	get { return new ValueCollection(this); }
        }
        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
        	get { return Values; }
        }
        
        public int Count
        {
            get { return _table.Size; }
        }
        
        public bool IsReadOnly
        {
            get { return false; }
        }
        
        public bool ContainsKey(TKey key)
        {
            TValue dummy;
            return Obtain(key, out dummy);
        }
        
        public void Add(TKey key, TValue value)
        {
            KV ret = PutIfMatch(new KV(key, value), KV.DeadKey, KVEqualityComparer.Default);
            if(ret != null && !(ret is TombstoneKV))
                throw new ArgumentException("An item with the same key has already been added.", "key");
        }
        
        public bool Remove(TKey key)
        {
        	KV ret = PutIfMatch(new TombstoneKV(key), KV.DeadKey, MatchesAll.Instance);
        	return ret != null && !(ret is TombstoneKV);
        }
        
        public bool TryGetValue(TKey key, out TValue value)
        {
            return Obtain(key, out value);
        }
        
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }
        
        public void Clear()
        {
            _table = new Table(_initialCapacity, new RefInt());
        }
        public bool Contains(KeyValuePair<TKey, TValue> item, IEqualityComparer<TValue> valueComparer)
        {
            TValue test;
            return Obtain(item.Key, out test) && valueComparer.Equals(item.Value, test);
        }
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return Contains(item, EqualityComparer<TValue>.Default);
        }
        
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(arrayIndex < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
        	Dictionary<TKey, TValue> snapshot = SnapshotDictionary();
        	TValue valForNull;
        	if(!typeof(TKey).IsValueType && TryGetValue(default(TKey), out valForNull))
        	{
	        	if(arrayIndex + snapshot.Count + 1 > array.Length)
	        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
	        	array[arrayIndex++] = new KeyValuePair<TKey, TValue>(default(TKey), valForNull);
        	}
        	((ICollection<KeyValuePair<TKey, TValue>>)snapshot).CopyTo(array, arrayIndex);
        }
        [SuppressMessage("Microsoft.Design", "CA1021")]
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
        public bool Remove(KeyValuePair<TKey, TValue> item, IEqualityComparer<TValue> valueComparer)
        {
            KeyValuePair<TKey, TValue> dummy;
            return Remove(item, valueComparer, out dummy);
        }
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
        public bool Remove(TKey key, TValue cmpValue, out TValue removed)
        {
        	return Remove(key, cmpValue, DefaultValCmp, out removed);
        }
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return Remove(item, DefaultValCmp);
        }
        public IEnumerable<KeyValuePair<TKey, TValue>> RemoveWhere(Func<TKey, TValue, bool> predicate)
        {
        	int removed = 0;
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
	            	{
	            		CopySlotsAndCheck(table, prime, idx);
	            	}
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
		            		else if( oldPair is TombstoneKV || !predicate(oldPair.Key, oldPair.Value))
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
	            		Resize(table, false);
	            	yield break;
	            }
        	}
        }
        public int Remove(Func<TKey, TValue, bool> predicate)
        {
        	int total = 0;
        	foreach(KeyValuePair<TKey, TValue> kvp in RemoveWhere(predicate))
        		++total;
        	return total;
        }
        internal sealed class KVEnumerator : IEnumerator<KV>, IEnumerable<KV>
        {
        	private LockFreeDictionary<TKey, TValue> _dict;
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
            void IEnumerator.Reset()
            {
            	throw new NotSupportedException();
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
        public struct LFDictionaryEnumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IEquatable<LFDictionaryEnumerator>
        {
            private KVEnumerator _src;
            internal LFDictionaryEnumerator(KVEnumerator src)
            {
                _src = src;
            }
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
            public void Dispose()
            {
                _src.Dispose();
            }
            public bool MoveNext()
            {
                return _src.MoveNext();
            }
            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }
            public bool Equals(LFDictionaryEnumerator other)
            {
            	return _src == other._src;
            }
            public override bool Equals(object obj)
			{
				return (obj is LFDictionaryEnumerator) && Equals((LFDictionaryEnumerator)obj);
			}
			public override int GetHashCode()
			{
				return _src.GetHashCode();
			}
			public static bool operator ==(LFDictionaryEnumerator lhs, LFDictionaryEnumerator rhs)
			{
				return lhs.Equals(rhs);
			}
			public static bool operator !=(LFDictionaryEnumerator lhs, LFDictionaryEnumerator rhs)
			{
				return !(lhs == rhs);
			}
        }
        public LFDictionaryEnumerator GetEnumerator()
        {
            return new LFDictionaryEnumerator(EnumerateKVs());
        }
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
	    public struct ValueCollection : ICollection<TValue>, IEquatable<ValueCollection>
	    {
	    	private readonly LockFreeDictionary<TKey, TValue> _dict;
	    	public ValueCollection(LockFreeDictionary<TKey, TValue> dict)
	    	{
	    		_dict = dict;
	    	}
			public int Count
			{
				get { return _dict.Count; }
			}
	    	
			public bool IsReadOnly
			{
				get { return true; }
			}
			public bool Contains(TValue item, IEqualityComparer<TValue> cmp)
			{
				foreach(TValue val in this)
					if(cmp.Equals(item, val))
						return true;
				return false;
			}
			public bool Contains(TValue item)
			{
				return Contains(item, DefaultValCmp);
			}
			public void CopyTo(TValue[] array, int arrayIndex)
			{
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(arrayIndex < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	Dictionary<TKey, TValue> snapshot = _dict.SnapshotDictionary();
	        	TValue valForNull;
	        	if(!typeof(TKey).IsValueType && _dict.TryGetValue(default(TKey), out valForNull))
	        	{
		        	if(arrayIndex + snapshot.Count + 1 > array.Length)
		        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
		        	array[arrayIndex++] = valForNull;
	        	}
	        	snapshot.Values.CopyTo(array, arrayIndex);
			}
			public struct ValueEnumerator : IEnumerator<TValue>, IEquatable<ValueEnumerator>
			{
	            private KVEnumerator _src;
	            internal ValueEnumerator(KVEnumerator src)
	            {
	                _src = src;
	            }
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
	            public void Dispose()
	            {
	                _src.Dispose();
	            }
	            public bool MoveNext()
	            {
	                return _src.MoveNext();
	            }
	            void IEnumerator.Reset()
	            {
	                throw new NotSupportedException();
	            }
				public bool Equals(ValueEnumerator other)
				{
					return object.Equals(this._src, other._src);
				}
	            public override bool Equals(object obj)
				{
					return obj is ValueEnumerator && Equals((ValueEnumerator)obj);
				}
				public override int GetHashCode()
				{
					return _src.GetHashCode();
				}
				public static bool operator ==(ValueEnumerator lhs, ValueEnumerator rhs)
				{
					return lhs.Equals(rhs);
				}
				public static bool operator !=(ValueEnumerator lhs, ValueEnumerator rhs)
				{
					return !(lhs == rhs);
				}

			}
			public ValueEnumerator GetEnumerator()
			{
				return new ValueEnumerator(_dict.EnumerateKVs());
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
			public bool Equals(ValueCollection other)
			{
				return _dict == other._dict;
			}
			public override bool Equals(object obj)
			{
				return (obj is ValueCollection) && Equals((ValueCollection)obj);
			}
			public override int GetHashCode()
			{
				return _dict.GetHashCode();
			}
			
			public static bool operator ==(ValueCollection lhs, ValueCollection rhs)
			{
				return lhs.Equals(rhs);
			}
			public static bool operator !=(ValueCollection lhs, ValueCollection rhs)
			{
				return !(lhs == rhs);
			}

	    }
	    public struct KeyCollection : ICollection<TKey>, IEquatable<KeyCollection>
	    {
	    	private readonly LockFreeDictionary<TKey, TValue> _dict;
	    	internal KeyCollection(LockFreeDictionary<TKey, TValue> dict)
	    	{
	    		_dict = dict;
	    	}
			public int Count
			{
				get { return _dict.Count; }
			}
			public bool IsReadOnly
			{
				get { return true; }
			}
			public bool Contains(TKey item)
			{
				return _dict.ContainsKey(item);
			}
			public void CopyTo(TKey[] array, int arrayIndex)
			{
	        	if(array == null)
	        		throw new ArgumentNullException("array");
	        	if(arrayIndex < 0)
	        		throw new ArgumentOutOfRangeException("arrayIndex");
	        	Dictionary<TKey, TValue> snapshot = _dict.SnapshotDictionary();
	        	if(!typeof(TKey).IsValueType && _dict.ContainsKey(default(TKey)))
	        	{
		        	if(arrayIndex + snapshot.Count + 1 > array.Length)
		        		throw new ArgumentException("The array is not large enough to contain the values that would be copied to it.");
	        		array[arrayIndex++] = default(TKey);
	        	}
	        	snapshot.Keys.CopyTo(array, arrayIndex);
			}
			public struct KeyEnumerator : IEnumerator<TKey>, IEquatable<KeyEnumerator>
			{
	            private KVEnumerator _src;
	            internal KeyEnumerator(KVEnumerator src)
	            {
	                _src = src;
	            }
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
	            public void Dispose()
	            {
	                _src.Dispose();
	            }
	            public bool MoveNext()
	            {
	                return _src.MoveNext();
	            }
	            void IEnumerator.Reset()
	            {
	                throw new NotSupportedException();
	            }
	            public bool Equals(KeyEnumerator other)
	            {
	            	return _src == other._src;
	            }
	            public override bool Equals(object obj)
				{
					return obj is KeyEnumerator && Equals((KeyEnumerator)obj);
				}
				public override int GetHashCode()
				{
					return _src.GetHashCode();
				}
	            
				public static bool operator ==(KeyEnumerator lhs, KeyEnumerator rhs)
				{
					return lhs.Equals(rhs);
				}
	            
				public static bool operator !=(KeyEnumerator lhs, KeyEnumerator rhs)
				{
					return !(lhs == rhs);
				}
			}
			public KeyEnumerator GetEnumerator()
			{
				return new KeyEnumerator(_dict.EnumerateKVs());
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
			public bool Equals(KeyCollection other)
			{
				return _dict == other._dict;
			}
			public override bool Equals(object obj)
			{
				return (obj is KeyCollection) && Equals((KeyCollection)obj);
			}
			public override int GetHashCode()
			{
				return _dict.GetHashCode();
			}
			public static bool operator ==(KeyCollection lhs, KeyCollection rhs)
			{
				return lhs.Equals(rhs);
			}
			public static bool operator !=(KeyCollection lhs, KeyCollection rhs)
			{
				return !(lhs == rhs);
			}
	    }
    }
}