// GroupedProducerConsumer.cs
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
using System.Threading;

namespace Ariadne.Collections
{
    /// <summary>A producer consumer that atomically delivers all pending items as a single enumeration./// </summary>
    public class GroupedProducerConsumer<T> : IProducerConsumerCollection<IEnumerable<T>>
    {
        private readonly SlimConcurrentQueue<T> _backing;
        /// <summary>
        /// Initialises a new instance of the <see cref="Ariadne.Collections.GroupedProducerConsumer{T}"/> class.
        /// </summary>
        public GroupedProducerConsumer()
        {
            _backing = new SlimConcurrentQueue<T>();
        }
        void IProducerConsumerCollection<IEnumerable<T>>.CopyTo(IEnumerable<T>[] array, int index)
        {
            Validation.CopyTo(array, index);
            array[index] = _backing.Snapshot();
        }
        /// <summary>Add a single item.</summary>
        /// <param name="item">Item to add.</param>
        public void Add(T item)
        {
            _backing.Enqueue(item);
        }
        /// <summary>Add a sequence of items.</summary>
        /// <param name="item"><see cref="IEnumerable{T}"/> of the items to add.</param>
        /// <remarks>The items are added as a single atomic operation.</remarks>
        public void Add(IEnumerable<T> item)
        {
            _backing.EnqueueRange(item);
        }
        bool IProducerConsumerCollection<IEnumerable<T>>.TryAdd(IEnumerable<T> item)
        {
            _backing.EnqueueRange(item);
            return true;
        }
        /// <summary>Attempt to remove a single item, obtaining the item.</summary>
        /// <returns>True if an item was removed, false if the collection was empty.</returns>
        /// <param name="item">The item removed.</param>
        public bool TryTake(out T item)
        {
            return _backing.TryDequeue(out item);
        }
        /// <summary>Attempt to remove all contained items, obtaining an enumeration of the items.</summary>
        /// <returns>True if the items were removed, false if the collection was empty.</returns>
        /// <param name="item">The items removed.</param>
        /// <remarks>The items will all be removed as a single atomic operation.</remarks>
        public bool TryTake(out IEnumerable<T> item)
        {
            var ret = _backing.AtomicDequeueAll();
            if(ret.Empty)
            {
                item = null;
                return false;
            }
            item = ret;
            return true;
        }
        IEnumerable<T>[] IProducerConsumerCollection<IEnumerable<T>>.ToArray()
        {
            return new IEnumerable<T>[]{ _backing.Snapshot() };
        }
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
            array.SetValue(_backing.Snapshot(), index);
        }
        /// <summary>Gets the number of items in the collection.</summary>
        /// <value>1 if there is a sequence of items contained, 0 otherwise.</value>
        /// <remarks>Since all items are retrieved as a single batch, this cannot be more than 1; adding more items just
        /// increases the size of the single sequence contained.</remarks>
        public int Count
        {
            get { return _backing.IsEmpty ? 0 : 1; }
        }
        object ICollection.SyncRoot
        {
            get { throw new NotSupportedException(Strings.SyncRootNotSupported); }
        }
        bool ICollection.IsSynchronized
        {
            get { return false; }
        }
        private class Enumerator : IEnumerator<SlimConcurrentQueue<T>.SnapshotEnumerable>, IEnumerator<IEnumerable<T>>
        {
            private readonly SlimConcurrentQueue<T> _queue;
            private bool _moved;
            public Enumerator(SlimConcurrentQueue<T> queue)
            {
                _queue = queue;
                _moved = false;
            }
            public SlimConcurrentQueue<T>.SnapshotEnumerable Current
            {
                get { return _queue.Snapshot(); }
            }
            IEnumerable<T> IEnumerator<IEnumerable<T>>.Current
            {
                get { return Current; }
            }
            public bool MoveNext()
            {
                if(_moved)
                    return false;
                _moved = true;
                return true;
            }
            public void Reset()
            {
                _moved = false;
            }
            object IEnumerator.Current
            {
                get { return Current; }
            }
            void IDisposable.Dispose()
            {
                // nop
            }
        }
        private Enumerator GetEnumerator()
        {
            return new Enumerator(_backing);
        }
        IEnumerator<IEnumerable<T>> IEnumerable<IEnumerable<T>>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}