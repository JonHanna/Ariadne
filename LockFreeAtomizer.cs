// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections;
using System.Collections.Generic;

namespace HackCraft.LockFree
{
    /// <summary>
    /// Allows for one copy of each distinct value of a reference type to be stored,
    /// reducing memory use and often speeding equality comparisons. Comparable to the
    /// string intern pool, but available for other types, and other equality comparisons
    /// (e.g. case-insensitive), removal of objects, and maintaining separate pools.
    /// </summary>
    [Serializable]
    public class LockFreeAtomizer<T> : ICollection<T>, ICloneable where T:class
    {
        public static int DefaultCapacity = LockFreeSet<T>.DefaultCapacity;
        private readonly LockFreeSet<T> _store;
        private LockFreeAtomizer(LockFreeSet<T> store)
        {
            _store = store;
        }
        public LockFreeAtomizer(int capacity, IEqualityComparer<T> comparer)
        {
            _store = new LockFreeSet<T>(capacity, comparer);
        }
        public LockFreeAtomizer(int capacity)
            :this(capacity, EqualityComparer<T>.Default){}
        public LockFreeAtomizer(IEqualityComparer<T> comparer)
            :this(DefaultCapacity, comparer){}
        public LockFreeAtomizer(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _store = new LockFreeSet<T>(collection, comparer);
        }
        public LockFreeAtomizer(IEnumerable<T> collection)
            :this(collection, EqualityComparer<T>.Default){}
        public T Atomize(T item)
        {
            return _store.FindOrStore(item);
        }
        public T IsAtomized(T item)
        {
            return _store.Find(item);
        }
        public bool Remove(T item)
        {
            return _store.Remove(item);
        }
        public LockFreeSet<T>.Enumerator GetEnumerator()
        {
            return _store.GetEnumerator();
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        public LockFreeAtomizer<T> Clone()
        {
            return new LockFreeAtomizer<T>(_store.Clone());
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
        public int Count
        {
            get { return _store.Count; }
        }
        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }
        void ICollection<T>.Add(T item)
        {
            Atomize(item);
        }
        public void Clear()
        {
            _store.Clear();
        }
        bool ICollection<T>.Contains(T item)
        {
            return _store.Contains(item);
        }
        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            _store.CopyTo(array, arrayIndex);
        }
    }
}
