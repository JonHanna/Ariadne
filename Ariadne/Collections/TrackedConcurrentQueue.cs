// © 2011–2014 Jon Hanna.
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
using System.Diagnostics;
using System.Threading;

namespace Ariadne.Collections
{
    /// <summary>A lock-free type-safe queue. This class is included mainly for completion, to allow for
    /// adoption to framework versions prior to the introduction of <see cref="ConcurrentQueue&lt;T>"/>
    /// and for use as the basis of other algorithms in this library. It does however also offer
    /// some other functionality.</summary>
    /// <typeparam name="T">The type of the values stored.</typeparam>
    /// <threadsafety static="true" instance="true"/>
    [Serializable]
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(DebuggerProxies.CollectionDebugView<>))]
    public sealed class TrackedConcurrentQueue<T> : IProducerConsumerCollection<T>
    {
        private readonly SlimConcurrentQueue<T> _backing;
        private int _count;

        /// <summary>Creates a new <see cref="TrackedConcurrentQueue&lt;T>"/></summary>
        public TrackedConcurrentQueue()
        {
            _backing = new SlimConcurrentQueue<T>();
        }

        /// <summary>Creates a new <see cref="TrackedConcurrentQueue&lt;T>"/> filled from the collection passed to it.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> that the queue will be filled from on construction.</param>
        public TrackedConcurrentQueue(IEnumerable<T> collection)
            : this()
        {
            _count = _backing.EnqueueRange(collection);
        }

        /// <summary>Adds an item to the end of the queue.</summary>
        /// <param name="item">The item to add.</param>
        public void Enqueue(T item)
        {
            _backing.Enqueue(item);
            Interlocked.Increment(ref _count);
        }

        /// <summary>Adds a collection of items to the queue.</summary>
        /// <param name="collection">The <see cref="IEnumerable&lt;T>"/> to add to the queue.</param>
        /// <remarks>The operation is not atomic, and may interleave with other enqueues or
        /// have some of the first items added dequeued before the last is enqueued.</remarks>
        public void EnqueueRange(IEnumerable<T> collection)
        {
            Interlocked.Add(ref _count, _backing.EnqueueRange(collection));
        }


        /// <summary>Attempts to obtain a the item at the start of the queue without removing it.</summary>
        /// <param name="item">The item found.</param>
        /// <returns>True if the method succeeds, false if the queue was empty.</returns>
        public bool TryPeek(out T item)
        {
            return _backing.TryPeek(out item);
        }

        /// <summary>Attempts to remove an item from the start of the queue.</summary>
        /// <param name="item">The item dequeued if successful.</param>
        /// <returns>True if the operation succeeds, false if the queue was empty.</returns>
        public bool TryDequeue(out T item)
        {
            if(_backing.TryDequeue(out item))
            {
                Interlocked.Decrement(ref _count);
                return true;
            }
            return false;
        }

        /// <summary>Gets a value indicating whether the queue has no items.</summary>
        /// <remarks>The operation is atomic, but may be stale by the time it returns.</remarks>
        /// <value>True if the queue has no item, false otherwise.</value>
        public bool IsEmpty
        {
            get { return _backing.IsEmpty; }
        }

        /// <summary>Gets the number of items in the queue.</summary>
        /// <value>The number of items in the queue.</value>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>Clears the queue as an atomic operation, and returns a <see cref="List&lt;T>"/> of the items removed.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the items removed.</returns>
        public List<T> DequeueToList()
        {
            return _backing.DequeueToList();
        }

        /// <summary>Copies the contents of the queue to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array at which to start copying.</param>
        /// <exception cref="ArgumentNullException">The array was null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The array index was less than zero.</exception>
        /// <exception cref="ArgumentException">The number of items in the collection was
        /// too great to copy into the array at the index given.</exception>
        /// <remarks>This method races with other threads as described for
        /// <see cref="SlimConcurrentQueue{T}.Snapshot()"/>.</remarks>
        public void CopyTo(T[] array, int arrayIndex)
        { 
            Validation.CopyTo(array, arrayIndex);
            new List<T>(_backing.Snapshot()).CopyTo(array, arrayIndex);
        }
        object ICollection.SyncRoot
        {
            get { throw new NotSupportedException(Strings.SyncRootNotSupported); }
        }
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }
        bool IProducerConsumerCollection<T>.TryAdd(T item)
        {
            Enqueue(item);
            return true;
        }
        bool IProducerConsumerCollection<T>.TryTake(out T item)
        {
            return TryDequeue(out item);
        }

        /// <summary>Returns an array of the current items in the queue without removing them.</summary>
        /// <returns>The array of the current items in the queue.</returns>
        /// <remarks>This method races with other threads as described for <see cref="Snapshot()"/>.</remarks>
        public T[] ToArray()
        {
            return new List<T>(Snapshot()).ToArray();
        }
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
            ((ICollection)new List<T>(Snapshot())).CopyTo(array, index);
        }
        /// <summary>Returns an enumerator that iterates through the collection.</summary>
        /// <returns>An <see cref="IEnumerator{T}"/>.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            return _backing.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        /// <summary>Returns a sequence of the queue at a point in time.</summary>
        /// <returns>An <see cref="IEnumerable{T}"/> of the elements in the queue.</returns>
        /// <remarks>This snapshot is only loosely timed, and may miss some dequeues that happened after the enqueue
        /// that resulted in the last item being added, or vice-versa.</remarks>
        public IEnumerable<T> Snapshot()
        {
            return _backing.Snapshot();
        }
    }
}