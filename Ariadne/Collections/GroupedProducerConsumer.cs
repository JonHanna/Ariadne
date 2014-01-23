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
    
    public class GroupedProducerConsumer<T> : IProducerConsumerCollection<IEnumerable<T>>
    {
        private readonly SlimConcurrentQueue<T> _backing;
        public GroupedProducerConsumer()
        {
            _backing = new SlimConcurrentQueue<T>();
        }
        public void CopyTo(IEnumerable<T>[] array, int index)
        {
            Validation.CopyTo(array, index);
            array[index] = _backing.Snapshot();
        }
        public void Add(T item)
        {
            _backing.Enqueue(item);
        }
        public void Add(IEnumerable<T> item)
        {
            _backing.EnqueueRange(item);
        }
        bool IProducerConsumerCollection<IEnumerable<T>>.TryAdd(IEnumerable<T> item)
        {
            _backing.EnqueueRange(item);
            return true;
        }
        public bool TryTake(out T item)
        {
            return _backing.TryDequeue(out item);
        }
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
        public IEnumerable<T>[] ToArray()
        {
            return new IEnumerable<T>[]{ _backing.Snapshot() };
        }
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
            ToArray().CopyTo(array, index);
        }
        /// <summary>Gets the number of items in the collection.</summary>
        /// <value>1 if there is a sequence of items contained, 0 otherwise.</value>
        /// <remarks>Since all items are retrieved as a single batch, this cannot be more than 1.</remarks>
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
        public struct Enumerator : IEnumerator<SlimConcurrentQueue<T>.SnapshotEnumerable>, IEnumerator<IEnumerable<T>>
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
        public Enumerator GetEnumerator()
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