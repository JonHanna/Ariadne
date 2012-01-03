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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Security;
using System.Security.Permissions;
using System.Threading;

namespace Ariadne
{
    //This queue is mostly for competion or for use in other classes in the library, considering that
    //the 4.0 FCL already has a lock-free queue.
    //The Mono implementation is very close to this, while the MS implementation is more complicated
    //but should offer make better use of CPU caches in cases where the same thread does multiple
    //enqueues or multiple dequeues in quick succession.

#pragma warning disable 420 // volatile semantics not lost as only by-ref calls are interlocked
    /// <summary>A lock-free type-safe queue. This class is included mainly for competion, to allow for
    /// adoption to framework versions prior to the introduction of <see cref="ConcurrentQueue&lt;T>"/>
    /// and for use as the basis of other algorithms in this library. It does however also offer
    /// some other functionality</summary>
    [Serializable]
    public sealed class LLQueue<T> : ICollection<T>, IProducerConsumerCollection<T>, ICloneable, ISerializable
    {
        private SinglyLinkedNode<T> _head;
        private SinglyLinkedNode<T> _tail;
        /// <summary>Creates a new <see cref="LLQueue&lt;T>"/></summary>
        public LLQueue()
        {
            _head = _tail = new SinglyLinkedNode<T>(default(T));
        }
        /// <summary>Creates a new <see cref="LLQueue&lt;T>"/> filled from the collection passed to it.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> that the queue will be filled from on construction.</param>
        public LLQueue(IEnumerable<T> collection)
            :this()
        {
            foreach(T item in collection)
                Enqueue(item);
        }
        private LLQueue(SerializationInfo info, StreamingContext context)
            :this((T[])info.GetValue("arr", typeof(T[]))){}
        [SecurityCritical]
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("arr", ToArray(), typeof(T[]));
        }
        /// <summary>Adds an item to the end of the queue.</summary>
        /// <param name="item">The item to add.</param>
        public void Enqueue(T item)
        {
            SinglyLinkedNode<T> newNode = new SinglyLinkedNode<T>(item);
            for(;;)
            {
                SinglyLinkedNode<T> curTail = _tail;
                if (Interlocked.CompareExchange(ref curTail.Next, newNode, null) == null)   //append to the tail if it is indeed the tail.
                {
                    Interlocked.CompareExchange(ref _tail, newNode, curTail);   //CAS in case we were assisted by an obstructed thread.
                    return;
                }
                else
                    Interlocked.CompareExchange(ref _tail, curTail.Next, curTail);  //assist obstructing thread.
            }
        }
        /// <summary>Adds a collection of items to the queue.</summary>
        /// <param name="items">The <see cref="IEnumerable&lt;T>"/> to add to the queue.</param>
        /// <remarks>The operation is not atomic, and may interleave with other enqueues or
        /// have some of the first items added dequeued before the last is enqueued.</remarks>
        public void EnqueueRange(IEnumerable<T> items)
        {
            if(items == null)
                throw new ArgumentNullException();
            foreach(T item in items)
                Enqueue(item);
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
                    else
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
        /// <summary>Tests whether the queue has no items.</summary>
        /// <remarks>The operation is atomic, but may be stale by the time it returns.</remarks>
        public bool IsEmpty
        {
            get
            {
                SinglyLinkedNode<T> head = _head;
                return head == _tail && head.Next == null;
            }
        }
        /// <summary>Clears the last item dequeued from the queue, allowing it to be collected.</summary>
        /// <remarks>The last item to be dequeued from the queue remains referenced by the queue. In the majority of cases, this will not be an issue,
        /// but calling this method will be necessary if:
        /// <list type="number">
        /// <item>The collection may be held in memory for some time.</item>
        /// <item>The collection may not be dequeued for some time.</item>
        /// <item>The collection consumes a large amount of memory that will not be cleared by disposing it.</item>
        /// </list>
        /// As a rule, this should almost never be necessary, and it is best avoided as the enumerating methods will then encounter a default
        /// value (null for a reference type) rather than that which was originally enqueued, but this method exists for this rare case.</remarks>
        public void ClearLastItem()
        {
            _head.Item = default(T);
        }
        /// <summary>An enumeration &amp; enumerator of items that were removed from the queue as an atomic operation.</summary>
        /// <remarks><see cref="AtomicDequeueAll"/> for more information.</remarks>
        public class AtDequeuEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private SinglyLinkedNode<T> _node;
            private SinglyLinkedNode<T> _end;
            internal AtDequeuEnumerator(LLQueue<T> queue)
            {
                SinglyLinkedNode<T> head = queue._head;
                for(;;)
                {
                    SinglyLinkedNode<T> tail = queue._tail;
                    SinglyLinkedNode<T> oldHead = Interlocked.CompareExchange(ref queue._head, tail, head);
                    if(oldHead == head)
                    {
                        _node = head;
                        _end = tail;
                        return;
                    }
                    else
                        head = oldHead;
                }
            }
            /// <summary>Returns the enumeration itself, as it is also it’s own enumerator.</summary>
            /// <returns>The enumeration itself.</returns>
            public AtDequeuEnumerator GetEnumerator()
            {
                return this;
            }
            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return this;
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }
            /// <summary>Returns the current item.</summary>
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
                //nop
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
        /// <summary>An enumeration &amp; enumerator of items that are removed from the queue as the enumeration is processed</summary>
        /// <remarks><see cref="DequeueAll"/> for more information.</remarks>
        public class DequeuEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private readonly LLQueue<T> _queue;
            private T _current;
            internal DequeuEnumerator(LLQueue<T> queue)
            {
                _queue = queue;
            }
            /// <summary>The current item.</summary>
            public T Current
            {
                get { return _current; }
            }
            object IEnumerator.Current
            {
                get { return _current; }
            }
            /// <summary>Moves to the next item in the enumeration (removing it from the queue)</summary>
            /// <returns>True if the method succeeds, false if the queue was empty</returns>
            /// <remarks>Since the class refers to the live state of the queue, after returning false
            /// it may return true on a subsequent call, if items were added in the meantime.</remarks>
            public bool MoveNext()
            {
                return _queue.TryDequeue(out _current);
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            /// <summary>Resets the enumeration.</summary>
            /// <remarks>Since the class refers to the live state of the queue, this is a non-operation
            /// as the enumeration is always attempting to dequeue from the front of the queue.</remarks>
            public void Reset()
            {
                //nop
            }
            /// <summary>Returns the enumeration itself, as it is also it’s own enumerator.</summary>
            /// <returns>The enumeration itself.</returns>
            public DequeuEnumerator GetEnumerator()
            {
                return this;
            }
            IEnumerator<T> IEnumerable<T>.GetEnumerator()
            {
                return this;
            }
            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }
        }
        /// <summary>An enumerator that enumerates through the queue, without removing them.</summary>
        /// <remarks>The enumerator is created based on the current front of the queue and continues
        /// until it reaches what is then the end. It may therefore on the one hand return items that
        /// have already been dequeued, and on the other never reach an end should new items be added
        /// frequently enough.</remarks>
        public class Enumerator : IEnumerator<T>
        {
            private readonly LLQueue<T> _queue;
            private SinglyLinkedNode<T> _node;
            internal Enumerator(LLQueue<T> queue)
            {
                _node = (_queue = queue)._head;
            }
            /// <summary>Returns the current item.</summary>
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
                    else
                        Interlocked.CompareExchange(ref _queue._tail, next, tail);
                }
                _node = next;
                return true;
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            /// <summary>Resets the enumeration to the current start of the queue.</summary>
            public void Reset()
            {
                _node = _queue._head;
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
        public DequeuEnumerator DequeueAll()
        {
            return new DequeuEnumerator(this);
        }
        /// <summary>Clears the queue as an atomic operation, and returns an enumeration of the items so-removed.</summary>
        /// <returns>A <see cref="AtDequeuEnumerator"/> that enumerates through the items removed.</returns>
        public AtDequeuEnumerator AtomicDequeueAll()
        {
            return new AtDequeuEnumerator(this);
        }
        /// <summary>Returns the count of the queue.</summary>
        /// <remarks>The operation is O(n), and counts from the start of the queue at the time the property is called,
        /// until the end of the queue at the time it reaches it. As such its utility in most cases is limited, and
        /// it can take a long (potentially unbounded) time to return if threads with higher priority are adding
        /// to the queue. If the count is greater than <see cref="int.MaxValue"/> it will return int.MaxValue.</remarks>
        public int Count
        {
            get
            {
                return CountUntil(int.MaxValue);
            }
        }
        /// <summary>Returns the count of the queue, or max, whichever is larger.</summary>
        /// <param name="max">The maximum count to count to.</param>
        /// <returns>This method is designed to deal with one of the problems with the <see cref="Count"/> property,
        /// being guaranteed to return once max is reached, even when racing with other threads.</returns>
        public int CountUntil(int max)
        {
            if(max < -1)
                throw new ArgumentOutOfRangeException("max");
            if(max == 0)
                return 0;
            int c = 0;
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                if(++c == max)
                    return max;
            return c;
        }
        /// <summary>Returns a <see cref="List&lt;T>"/> of the current items in the queue without removing them.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the current items in the queue.</returns>
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>.</remarks>
        public List<T> ToList()
        {
        	//As an optimisation, if you past an ICollection<T> to List<T>’s constructor that takes an IEnumerable<T>
        	//it casts to ICollection<T> and calls Count on it to decide on the initial capacity. Since our Count
        	//is O(n) and since we could grow in the meantime, this optimisation actually makes things worse, so we avoid it.
        	List<T> list = new List<T>();
        	//AddRange has the same problem...
        	Enumerator en = new Enumerator(this);
        	while(en.MoveNext())
        	    list.Add(en.Current);
        	return list;
        }
        /// <summary>Clears the queue as an atomic operation, and returns a <see cref="List&lt;T>"/> of the items removed.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the items removed.</returns>
        public List<T> DequeueToList()
        {
            return new List<T>(AtomicDequeueAll());
        }
        /// <summary>Creates a new queue with the same items as this one.</summary>
        /// <returns>A new <see cref="LLQueue&lt;T>"/>.</returns>
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>. Use
        /// <see cref="Transfer"/> to create a copy of the queue while clearing it atomically.</remarks>
        public LLQueue<T> Clone()
        {
            return new LLQueue<T>(this);
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
        /// <summary>Clears the queue as a single atomic operation, and returns a queue with the same contents as those removed.</summary>
        /// <returns>The new queue.</returns>
        public LLQueue<T> Transfer()
        {
            return new LLQueue<T>(AtomicDequeueAll());
        }
        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }
        void ICollection<T>.Add(T item)
        {
            Enqueue(item);
        }
        /// <summary>Clears the queue as a single atomic operation.</summary>
        public void Clear()
        {
            SinglyLinkedNode<T> head = _head;
            SinglyLinkedNode<T> oldHead;
            while((oldHead = Interlocked.CompareExchange(ref _head, _tail, head)) != head)
                head = oldHead;
        }
        /// <summary>Examines the queue for the presence of an item.</summary>
        /// <param name="item">The item to search for.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;T>"/> to use to compare
        /// the item with those in the collections.</param>
        /// <returns>True if the item was found, false otherwise.</returns>
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>.</remarks>
        public bool Contains(T item, IEqualityComparer<T> comparer)
        {
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                if(comparer.Equals(en.Current, item))
                    return true;
            return false;
        }
        /// <summary>Examines the queue for the presence of an item.</summary>
        /// <param name="item">The item to search for.</param>
        /// <returns>True if the item was found, false otherwise.</returns>
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>.</remarks>
        public bool Contains(T item)
        {
            return Contains(item, EqualityComparer<T>.Default);
        }
        /// <summary>Copies the contents of the queue to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array to start copying from</param>
        /// <exception cref="System.ArgumentNullException"/>The array was null.
        /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
        /// <exception cref="System.ArgumentException"/>The number of items in the collection was
        /// too great to copy into the array at the index given.
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>.</remarks>
        public void CopyTo(T[] array, int arrayIndex)
        {
            Validation.CopyTo(array, arrayIndex);
        	ToList().CopyTo(array, arrayIndex);
        }
        bool ICollection<T>.Remove(T item)
        {
            throw new NotSupportedException();
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
            Enqueue(item);
            return true;
        }
        bool IProducerConsumerCollection<T>.TryTake(out T item)
        {
            return TryDequeue(out item);
        }
        /// <summary>Returns an array of the current items in the queue without removing them.</summary>
        /// <returns>The array of the current items in the queue.</returns>
        /// <remarks>This method races with other threads as described for <see cref="GetEnumerator"/>.</remarks>
        public T[] ToArray()
        {
            return ToList().ToArray();
        }
        void ICollection.CopyTo(Array array, int index)
        {
            Validation.CopyTo(array, index);
        	((ICollection)ToList()).CopyTo(array, index);
        }
    }
#pragma warning restore 420
}
