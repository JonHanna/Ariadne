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

namespace HackCraft.LockFree
{
    //This queue is mostly for competion or for use in other classes in the library, considering that
    //the 4.0 FCL already has a lock-free queue.
    //The Mono implementation is very close to this, while the MS implementation is more complicated
    //but should offer make better use of CPU caches in cases where the same thread does multiple
    //enqueues or multiple dequeues in quick succession.

#pragma warning disable 420 // volatile semantics not lost as only by-ref calls are interlocked
    [Serializable]
    public sealed class LLQueue<T> : ICollection<T>, IProducerConsumerCollection<T>, ICloneable, ISerializable
    {
        private SinglyLinkedNode<T> _head;
        private SinglyLinkedNode<T> _tail;
        public LLQueue()
        {
            _head = _tail = new SinglyLinkedNode<T>(default(T));
        }
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
        public void EnqueueRange(IEnumerable<T> items)
        {
            if(items == null)
                throw new ArgumentNullException();
            foreach(T item in items)
                Enqueue(item);
        }
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
                    //There are two schools of thought on this. On the one hand, setting the item in this node
                    //to default means that we don't have the situation where objects get left in memory for a long time.
                    //On the other hand it's only one item, and won't be for long in most situations, so we should just
                    //wait. On the gripping hand, it's a cheap operation.
                    curHeadNext.Item = default(T);
                    return true;
                }
            }
        }
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
        public bool IsEmpty
        {
            get
            {
                SinglyLinkedNode<T> head = _head;
                return head == _tail && head.Next == null;
            }
        }
        public class AtDequeuEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private SinglyLinkedNode<T> _node;
            private SinglyLinkedNode<T> _end;
            public AtDequeuEnumerator(LLQueue<T> queue)
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
                _end.Item = default(T);
            }
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
        public class DequeuEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private readonly LLQueue<T> _queue;
            private T _current;
            public DequeuEnumerator(LLQueue<T> queue)
            {
                _queue = queue;
            }
            public T Current
            {
                get { return _current; }
            }
            object IEnumerator.Current
            {
                get { return _current; }
            }
            public bool MoveNext()
            {
                return _queue.TryDequeue(out _current);
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            public void Reset()
            {
                //nop
            }
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
        public class Enumerator : IEnumerator<T>
        {
            private readonly LLQueue<T> _queue;
            private SinglyLinkedNode<T> _node;
            internal Enumerator(LLQueue<T> queue)
            {
                _node = (_queue = queue)._head;
            }
            public T Current
            {
                get { return _node.Item; }
            }
            object IEnumerator.Current
            {
                get { return _node.Item; }
            }
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
            public void Reset()
            {
                _node = _queue._head;
            }
        }
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
        public DequeuEnumerator DequeueAll()
        {
            return new DequeuEnumerator(this);
        }
        public AtDequeuEnumerator AtomicDequeueAll()
        {
            return new AtDequeuEnumerator(this);
        }
        public int Count
        {
            get
            {
                int c = 0;
                Enumerator en = new Enumerator(this);
                while(en.MoveNext())
                    ++c;
                return c;
            }
        }
        public List<T> ToList()
        {
        	//As an optimisation, if you past an ICollection<T> to List<T>'s constructor that takes an IEnumerable<T>
        	//it casts to ICollection<T> and calls Count on it to decide on the initial capacity. Since our Count
        	//is O(n) and since we could grow in the meantime, this optimisation actually makes things worse, so we avoid it.
        	List<T> list = new List<T>();
        	//AddRange has the same problem...
        	Enumerator en = new Enumerator(this);
        	while(en.MoveNext())
        	    list.Add(en.Current);
        	return list;
        }
        public List<T> DequeueToList()
        {
            return new List<T>(AtomicDequeueAll());
        }
        public LLQueue<T> Clone()
        {
            return new LLQueue<T>(this);
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
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
        public void Clear()
        {
            SinglyLinkedNode<T> head = _head;
            SinglyLinkedNode<T> oldHead;
            while((oldHead = Interlocked.CompareExchange(ref _head, _tail, head)) != head)
                head = oldHead;
            _head.Item = default(T);
        }
        public bool Contains(T item, IEqualityComparer<T> comparer)
        {
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                if(comparer.Equals(en.Current, item))
                    return true;
            return false;
        }
        public bool Contains(T item)
        {
            return Contains(item, EqualityComparer<T>.Default);
        }
        public void CopyTo(T[] array, int arrayIndex)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(arrayIndex < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
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
        public T[] ToArray()
        {
            return ToList().ToArray();
        }
        void ICollection.CopyTo(Array array, int index)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(array.Rank != 1)
        	    throw new ArgumentException(Strings.Cant_Copy_Multidimensional, "array");
        	if(array.GetLowerBound(0) != 0)
        	    throw new ArgumentException(Strings.Cant_Copy_NonZero, "array");
        	if(index < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
        	((ICollection)ToList()).CopyTo(array, index);
        }
    }
#pragma warning restore 420
}
