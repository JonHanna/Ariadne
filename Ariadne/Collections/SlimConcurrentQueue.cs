// SlimConcurrentQueue.cs
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
using System.Runtime.Serialization;
using System.Security;
using System.Security.Permissions;
using System.Threading;

namespace Ariadne.Collections
{
// volatile semantics not lost as only by-ref calls are interlocked
#pragma warning disable 420
    /// <summary>Simple concurrent queue.</summary>
    [Serializable]
    public sealed class SlimConcurrentQueue<T> : IEnumerable<T>, ISerializable
    {
        private SinglyLinkedNode<T> _head;
        private SinglyLinkedNode<T> _tail;
        /// <summary>Initialises a new instance of the <see cref="SlimConcurrentQueue{T}"/> class.</summary>
        public SlimConcurrentQueue()
        {
            _head = _tail = new SinglyLinkedNode<T>(default(T));
        }
        /// <summary>Initialises a new instance of the <see cref="SlimConcurrentQueue{T}"/> class.</summary>
        /// <param name="collection">Sequence of elements to fill the queue with upon creation.</param>
        public SlimConcurrentQueue(IEnumerable<T> collection)
            : this()
        {
            EnqueueRange(collection);
        }
        /// <summary>Initialises a new instance of the <see cref="SlimConcurrentQueue{T}"/> class.</summary>
        private SlimConcurrentQueue(SerializationInfo info, StreamingContext context)
            : this((T[])info.GetValue("arr", typeof(T[])))
        {
        }
        [SecurityCritical]
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("arr", new List<T>(Snapshot()).ToArray(), typeof(T[]));
        }
        /// <summary>Adds an item to the end of the queue.</summary>
        /// <param name="item">The item to add.</param>
        public void Enqueue(T item)
        {
            var newNode = new SinglyLinkedNode<T>(item);
            for(;;)
            {
                SinglyLinkedNode<T> curTail = _tail;

                // append to the tail if it is indeed the tail.
                if (Interlocked.CompareExchange(ref curTail.Next, newNode, null) == null)
                {
                    // CAS in case we were assisted by an obstructed thread.
                    Interlocked.CompareExchange(ref _tail, newNode, curTail);   
                    return;
                }
                Interlocked.CompareExchange(ref _tail, curTail.Next, curTail);  // assist obstructing thread.
            }
        }
        /// <summary>Adds a collection of items to the queue.</summary>
        /// <param name="collection">The <see cref="IEnumerable&lt;T>"/> to add to the queue.</param>
        /// <remarks>The operation is not atomic, and may interleave with other enqueues or
        /// have some of the first items added dequeued before the last is enqueued.</remarks>
        public int EnqueueRange(IEnumerable<T> collection)
        {
            Validation.NullCheck(collection, "collection");
            SinglyLinkedNode<T> start;
            SinglyLinkedNode<T> end;
            int tally;
            using(IEnumerator<T> en = collection.GetEnumerator())
            {
                if(!en.MoveNext())
                    return 0;
                start = end = new SinglyLinkedNode<T>(en.Current);
                tally = 1;
                while(en.MoveNext())
                {
                    end = end.Next = new SinglyLinkedNode<T>(en.Current);
                    ++tally;
                }
            }
            for(;;)
            {
                SinglyLinkedNode<T> curTail = _tail;
                if(Interlocked.CompareExchange(ref curTail.Next, start, null) == null)
                {
                    for(;;)
                    {
                        SinglyLinkedNode<T> newTail = Interlocked.CompareExchange(ref _tail, end, curTail);
                        if(newTail == curTail || newTail.Next == null)
                            return tally;
                        end = newTail;
                        for(SinglyLinkedNode<T> next = end.Next; next != null; next = (end = next).Next)
                        {
                        }
                    }
                }
                Interlocked.CompareExchange(ref _tail, curTail.Next, curTail);
            }
        }

        /// <summary>Attempts to remove an item from the start of the queue.</summary>
        /// <param name="item">The item dequeued if successful.</param>
        /// <returns>True if the operation succeeds, false if the queue was empty.</returns>
        public bool TryDequeue(out T item)
        {
            for(;;)
            {
                SinglyLinkedNode<T> curHead = _head;
                SinglyLinkedNode<T> curTail = _tail;
                SinglyLinkedNode<T> curHeadNext = curHead.Next;
                if (curHead == curTail)
                {
                    if (curHeadNext == null)
                    {
                        item = default(T);
                        return false;
                    }
                    Interlocked.CompareExchange(ref _tail, curHeadNext, curTail);   // assist obstructing thread
                }
                if(Interlocked.CompareExchange(ref _head, curHeadNext, curHead) == curHead)
                {
                    item = curHeadNext.Item;
                    return true;
                }
            }
        }
        /// <summary>Attempts to obtain a the item at the start of the queue without removing it.</summary>
        /// <param name="item">The item found.</param>
        /// <returns>True if the method succeeds, false if the queue was empty.</returns>
        public bool TryPeek(out T item)
        {
            SinglyLinkedNode<T> node = _head.Next;
            if(node == null)
            {
                item = default(T);
                return false;
            }
            item = node.Item;
            return true;
        }
        /// <summary>Gets a value indicating whether the queue has no items.</summary>
        /// <remarks>The operation is atomic, but may be stale by the time it returns.</remarks>
        /// <value>True if the queue has no item, false otherwise.</value>
        public bool IsEmpty
        {
            get
            {
                SinglyLinkedNode<T> head = _head;
                return head == _tail & head.Next == null;
            }
        }
        /// <summary>Clears the queue as a single atomic operation.</summary>
        public void Clear()
        {
            SinglyLinkedNode<T> head = _head;
            SinglyLinkedNode<T> oldHead;
            while((oldHead = Interlocked.CompareExchange(ref _head, _tail, head)) != head)
                head = oldHead;
        }
        /// <summary>An enumeration of items that were removed from the queue as an atomic operation.</summary>
        /// <remarks><see cref="AtomicDequeueAll"/> for more information.</remarks>
        /// <threadsafety static="true" instance="true"/>
        /// <tocexclude/>
        public struct AtDequeuEnumerable : IEnumerable<T>
        {
            private readonly SinglyLinkedNode<T> _start;
            private readonly SinglyLinkedNode<T> _end;
            internal AtDequeuEnumerable(SlimConcurrentQueue<T> queue)
            {
                SinglyLinkedNode<T> head;
                SinglyLinkedNode<T> tail;
                while(Interlocked.CompareExchange(ref queue._head, tail = queue._tail, head = queue._head) != head)
                {
                }
                _start = head;
                _end = tail;
            }

            internal bool Empty
            {
                get { return _start == _end; }
            }

            /// <summary>Returns an enumerator that iterates through the collection.</summary>
            /// <returns>An <see cref="AtDequeuEnumerator"/>.</returns>
            public AtDequeuEnumerator GetEnumerator()
            {
                return new AtDequeuEnumerator(_start, _end);
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

        /// <summary>An enumerator of items that were removed from the queue as an atomic operation.</summary>
        /// <remarks><see cref="AtomicDequeueAll"/> for more information.</remarks>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public sealed class AtDequeuEnumerator : IEnumerator<T>
        {
            private readonly SinglyLinkedNode<T> _end;
            private SinglyLinkedNode<T> _node;
            internal AtDequeuEnumerator(SinglyLinkedNode<T> start, SinglyLinkedNode<T> end)
            {
                _node = start;
                _end = end;
            }

            /// <summary>Gets the current element being enumerated.</summary>
            /// <value>The current element enumerated.</value>
            public T Current
            {
                get { return _node.Item; }
            }
            object IEnumerator.Current
            {
                get { return _node.Item; }
            }
            void IDisposable.Dispose()
            {
                // nop
            }

            /// <summary>Moves through the enumeration to the next item.</summary>
            /// <returns>True if another item was found, false if the end of the enumeration was reached.</returns>
            public bool MoveNext()
            {
                if(_node == _end)
                    return false;
                _node = _node.Next;
                return true;
            }
            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>An enumeration of items that are removed from the queue as the enumeration is processed.</summary>
        /// <remarks><see cref="DequeueAll"/> for more information.</remarks>
        /// <threadsafety static="true" instance="true"/>
        /// <tocexclude/>
        public struct DequeuEnumerable : IEnumerable<T>
        {
            private readonly SlimConcurrentQueue<T> _queue;
            internal DequeuEnumerable(SlimConcurrentQueue<T> queue)
            {
                _queue = queue;
            }

            /// <summary>Returns an enumerator that iterates through the enumeration, dequeuing items as it returns them.</summary>
            /// <returns>An <see cref="DequeuEnumerator"/>.</returns>
            public DequeuEnumerator GetEnumerator()
            {
                return new DequeuEnumerator(_queue);
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

        /// <summary>An enumerator of items that are removed from the queue as the enumeration is processed.</summary>
        /// <remarks><see cref="DequeueAll"/> for more information.</remarks>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public sealed class DequeuEnumerator : IEnumerator<T>
        {
            private readonly SlimConcurrentQueue<T> _queue;
            private T _current;
            internal DequeuEnumerator(SlimConcurrentQueue<T> queue)
            {
                _queue = queue;
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

            /// <summary>Moves to the next item in the enumeration (removing it from the queue).</summary>
            /// <returns>True if the method succeeds, false if the queue was empty.</returns>
            /// <remarks>Since the class refers to the live state of the queue, after returning false
            /// it may return true on a subsequent call, if items were added in the meantime.</remarks>
            public bool MoveNext()
            {
                return _queue.TryDequeue(out _current);
            }
            void IDisposable.Dispose()
            {
                // nop
            }

            /// <summary>Resets the enumeration.</summary>
            /// <remarks>Since the class refers to the live state of the queue, this is a non-operation
            /// as the enumeration is always attempting to dequeue from the front of the queue.</remarks>
            public void Reset()
            {
                // nop
            }
        }

        /// <summary>An enumerator that enumerates through the queue, without removing them.</summary>
        /// <remarks>The enumerator is created based on the current front of the queue and continues
        /// until it reaches what is then the end. It may therefore on the one hand return items that
        /// have already been dequeued, and on the other never reach an end should new items be added
        /// frequently enough.</remarks>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself, though its methods may be called
        /// concurrently with other operations on the same collection.</threadsafety>
        /// <tocexclude/>
        public sealed class Enumerator : IEnumerator<T>
        {
            private readonly SlimConcurrentQueue<T> _queue;
            private SinglyLinkedNode<T> _node;
            internal Enumerator(SlimConcurrentQueue<T> queue)
            {
                _node = (_queue = queue)._head;
            }

            /// <summary>Gets the current element being enumerated.</summary>
            /// <value>The current element being enumerated.</value>
            public T Current
            {
                get { return _node.Item; }
            }
            object IEnumerator.Current
            {
                get { return _node.Item; }
            }

            /// <summary>Moves to the next item.</summary>
            /// <returns>True if an item is found, false if it reaches the end of the queue.</returns>
            public bool MoveNext()
            {
                SinglyLinkedNode<T> tail = _queue._tail;
                SinglyLinkedNode<T> next = _node.Next;
                if(_node == tail)
                {
                    if(next == null)
                        return false;
                    Interlocked.CompareExchange(ref _queue._tail, next, tail);
                }
                _node = next;
                return true;
            }
            void IDisposable.Dispose()
            {
                // nop
            }

            /// <summary>Resets the enumeration to the current start of the queue.</summary>
            public void Reset()
            {
                _node = _queue._head;
            }
        }

        /// <summary>Enumerator that iterates through a <see cref="SnapshotEnumerable"/>.</summary>
        public sealed class SnapshotEnumerator : IEnumerator<T>
        {
            private readonly SinglyLinkedNode<T> _head;
            private readonly SinglyLinkedNode<T> _tail;
            private SinglyLinkedNode<T> _node;
            internal SnapshotEnumerator(SinglyLinkedNode<T> head, SinglyLinkedNode<T> tail)
            {
                _node = _head = head;
                _tail = tail;
                _node = _head;
            }

            /// <summary>Gets the current element being enumerated.</summary>
            /// <value>The current element being enumerated.</value>
            public T Current
            {
                get { return _node.Item; }
            }
            object IEnumerator.Current
            {
                get { return _node.Item; }
            }

            /// <summary>Moves to the next item.</summary>
            /// <returns>True if an item is found, false if it reaches the end of the queue.</returns>
            public bool MoveNext()
            {
                if(_node == _tail)
                    return false;
                _node = _node.Next;
                return true;
            }
            void IDisposable.Dispose()
            {
                // nop
            }

            /// <summary>Resets the enumeration to the current start of the queue.</summary>
            public void Reset()
            {
                _node = _head;
            }
        }
        /// <summary>Enumerable representing the queue at a given point in time, as seen from the calling thread.
        /// </summary>
        /// <remarks>This snapshot is only loosely timed, and may miss some dequeues that happened after the enqueue
        /// that resulted in the last item being added, or vice-versa.</remarks>
        public struct SnapshotEnumerable : IEnumerable<T>
        {
            private readonly SinglyLinkedNode<T> _head;
            private readonly SinglyLinkedNode<T> _tail;
            internal SnapshotEnumerable(SlimConcurrentQueue<T> queue)
            {
                _head = queue._head;
                _tail = queue._tail;
            }
            /// <summary>Returns an enumerator that iterates through a collection.</summary>
            /// <returns>The enumerator.</returns>
            public SnapshotEnumerator GetEnumerator()
            {
                return new SnapshotEnumerator(_head, _tail);
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
        /// <summary>Returns an object that enumerates the queue without removing items.</summary>
        /// <returns>An <see cref="Enumerator"/> that starts with the current start of the queue.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>Returns an enumerator that removes items from the queue as it returns them.</summary>
        /// <returns>A <see cref="DequeuEnumerator"/> that removes from the queue as it is processed.</returns>
        /// <remarks>The operation is not atomic and will interleave with other dequeue operations, and can
        /// return items enqueued after the method was called. Use <see cref="AtomicDequeueAll"/> if you
        /// want to clear the queue as a single atomic operation.</remarks>
        public DequeuEnumerable DequeueAll()
        {
            return new DequeuEnumerable(this);
        }

        /// <summary>Clears the queue as an atomic operation, and returns an enumeration of the items so-removed.</summary>
        /// <returns>A <see cref="AtDequeuEnumerator"/> that enumerates through the items removed.</returns>
        public AtDequeuEnumerable AtomicDequeueAll()
        {
            return new AtDequeuEnumerable(this);
        }
        /// <summary>Clears the queue as a single atomic operation, and returns a queue with the same contents as those removed.</summary>
        /// <returns>The new queue.</returns>
        public SlimConcurrentQueue<T> Transfer()
        {
            return new SlimConcurrentQueue<T>(AtomicDequeueAll());
        }
        /// <summary>Clears the queue as an atomic operation, and returns a <see cref="List&lt;T>"/> of the items removed.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the items removed.</returns>
        public List<T> DequeueToList()
        {
            return new List<T>(AtomicDequeueAll());
        }
        /// <summary>Returns a sequence of the queue at a point in time.</summary>
        /// <returns>A <see cref="SnapshotEnumerable"/> of the elements in the queue.</returns>
        /// <remarks>This snapshot is only loosely timed, and may miss some dequeues that happened after the enqueue
        /// that resulted in the last item being added, or vice-versa.</remarks>
        public SnapshotEnumerable Snapshot()
        {
            return new SnapshotEnumerable(this);
        }
    }
    
#pragma warning restore 420
}
