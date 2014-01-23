// UniqueElementProducerConsumer.cs
//
// Author:
//     Jon Hanna <jon@hackcraft.net>
//
// © 2014 Jon Hanna
//
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Ariadne.Collections
{
    /// <summary>A producer-consumer collection that will only have a single instance of a given element, at any point
    /// in time.</summary>
    /// <typeparam name="T">The type of the values stored.</typeparam>
    public class UniqueElementProducerConsumer<T> : IProducerConsumerCollection<T>
    {
        private readonly ThreadSafeSet<T> _store;

        /// <summary>Initialises a new instance of the <see cref="UniqueElementProducerConsumer{T}"/> class.</summary>
        /// <param name="collection">Collection to fill the <see cref="UniqueElementProducerConsumer{T}"/> with upon
        /// creation.</param>
        /// <param name="comparer">Comparer to determine uniqueness of elements.</param>
        public UniqueElementProducerConsumer(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _store = new ThreadSafeSet<T>(collection, comparer);
        }

        /// <summary>Initialises a new instance of the <see cref="UniqueElementProducerConsumer{T}"/> class.</summary>
        /// <param name="comparer">Comparer to determine uniqueness of elements.</param>
        public UniqueElementProducerConsumer(IEqualityComparer<T> comparer)
        {
            _store = new ThreadSafeSet<T>(comparer);
        }

        /// <summary>Initialises a new instance of the <see cref="UniqueElementProducerConsumer{T}"/> class.</summary>
        /// <param name="collection">Collection to fill the <see cref="UniqueElementProducerConsumer{T}"/> with upon
        /// creation.</param>
        public UniqueElementProducerConsumer(IEnumerable<T> collection)
        {
            _store = new ThreadSafeSet<T>(collection);
        }

        /// <summary>Initialises a new instance of the <see cref="UniqueElementProducerConsumer{T}"/> class.</summary>
        public UniqueElementProducerConsumer()
        {
            _store = new ThreadSafeSet<T>();
        }

        /// <summary>Tries to add a new item to the collection.</summary>
        /// <returns>True if the element was added, false if there was already a matching element.</returns>
        /// <param name="item">Item to add.</param>
        public bool TryAdd(T item)
        {
            return _store.Add(item);
        }

        /// <summary>Attempts to take a single item from the set.</summary>
        /// <param name="item">On return, the item removed, if successful.</param>
        /// <returns>True if an item was removed, false if the set had been empty.</returns>
        /// <remarks>The item returned is arbitrarily determined, with no guaranteed ordering.</remarks>
        public bool TryTake(out T item)
        {
            return _store.TryTake(out item);
        }
        T[] IProducerConsumerCollection<T>.ToArray()
        {
            return _store.ToArray();
        }
        void IProducerConsumerCollection<T>.CopyTo(T[] array, int index)
        {
            _store.CopyTo(array, index);
        }
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
            ((ICollection)_store.ToHashSet()).CopyTo(array, index);
        }

        /// <summary>Gets the number of elements in the collection.</summary>
        /// <value>The number of elements in the collection.</value>
        public int Count
        {
            get { return _store.Count; }
        }
        object ICollection.SyncRoot
        {
            get { throw new NotSupportedException(Strings.SyncRootNotSupported); }
        }
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }

        /// <summary>Returns an enumerator that iterates through the collection.</summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return _store.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}