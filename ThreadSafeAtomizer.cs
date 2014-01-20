// © 2011 Jon Hanna.
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections.Generic;
using Ariadne.Collections;

namespace Ariadne
{
    /// <summary>Allows for one copy of each distinct value of a reference type to be stored,
    /// reducing memory use and often speeding equality comparisons. It can be the basis of
    /// the flyweight pattern, and similar. Comparable to the
    /// string intern pool, but available for other types, and other equality comparisons
    /// (e.g. case-insensitive), removal of objects, and maintaining separate pools.
    /// This is a wrapper around <see cref="ThreadSafeSet&lt;T>"/> (the implementation may change in the
    /// future to wrap another set-type class), that calls into the FindOrStore and Find
    /// methods, which exist with exactly this sort of functionality in mind - considering
    /// the lack of such functionality in <see cref="HashSet&lt;T>"/> to be a lack.</summary>
    /// <typeparam name="T">The type of the values stored (must be a reference type).</typeparam>
    /// <threadsafety static="true" instance="true"/>
    [Serializable]
    public sealed class ThreadSafeAtomizer<T> : ICloneable where T:class
    {
        private readonly ThreadSafeSet<T> _store;
        private ThreadSafeAtomizer(ThreadSafeSet<T> store)
        {
            _store = store;
        }
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/>.</summary>
        /// <param name="capacity">The initial capacity of the atomizer.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;T>"/> to use when comparing items
        /// added to the store.</param>
        public ThreadSafeAtomizer(int capacity, IEqualityComparer<T> comparer)
        {
            _store = new ThreadSafeSet<T>(capacity, comparer);
        }
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/>.</summary>
        /// <param name="capacity">The initial capacity of the atomizer.</param>
        public ThreadSafeAtomizer(int capacity)
            :this(capacity, EqualityComparer<T>.Default){}
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/>.</summary>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;T>"/> to use when comparing items
        /// added to the store.</param>
        public ThreadSafeAtomizer(IEqualityComparer<T> comparer)
        {
            _store = new ThreadSafeSet<T>(comparer);
        }
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/>.</summary>
        public ThreadSafeAtomizer()
            :this(EqualityComparer<T>.Default){}
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/> and fills it from the collection passed.</summary>
        /// <param name="collection">The <see cref="IEnumerable&lt;T>"/> to fill the atomizer with on construction.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;T>"/> to use when comparing items
        /// added to the collection.</param>
        public ThreadSafeAtomizer(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _store = new ThreadSafeSet<T>(collection, comparer);
        }
        /// <summary>Creates a new <see cref="ThreadSafeAtomizer&lt;T>"/> and fills it from the collection passed.</summary>
        /// <param name="collection">The <see cref="IEnumerable&lt;T>"/> to fill the atomizer with on construction.</param>
        public ThreadSafeAtomizer(IEnumerable<T> collection)
            :this(collection, EqualityComparer<T>.Default){}
        /// <summary>Searches for an equivalent item, adding it if not present, and returning either the item found
        /// or the item passed if there was none that matched.</summary>
        /// <param name="item">The item to store.</param>
        /// <returns>The item passed, or an equivalent item if one already exists.</returns>
        public T Atomize(T item)
        {
            return _store.FindOrStore(item);
        }
        /// <summary>Returns an equivalent item if it exists in the store, or null if none is present.</summary>
        /// <param name="item">The item to search for.</param>
        /// <returns>An equivalent item if it exists in the store, or null if none is present.</returns>
        public T IsAtomized(T item)
        {
            return _store.Find(item);
        }
        /// <summary>Removes an item from the store.</summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if an item was removed, false if no equivalent item was found.</returns>
        public bool Remove(T item)
        {
            return _store.Remove(item);
        }
        /// <summary>Returns another <see cref="ThreadSafeAtomizer&lt;T>"/> with the same contents as this.</summary>
        /// <returns>The copied <see cref="ThreadSafeAtomizer&lt;T>"/>.</returns>
        public ThreadSafeAtomizer<T> Clone()
        {
            return new ThreadSafeAtomizer<T>(_store.Clone());
        }
        /// <summary>The number of items in the store.</summary>
        public int Count
        {
            get { return _store.Count; }
        }
        /// <summary>Removes all items from the store.</summary>
        public void Clear()
        {
            _store.Clear();
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
    }
}
