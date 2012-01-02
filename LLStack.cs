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
    //This stack is mostly for competion or for use in other classes in the library, considering that
    //the 4.0 FCL already has a lock-free stack.
    
    [Serializable]
    public class LLStack<T> : ICollection<T>, IProducerConsumerCollection<T>, ICloneable, ISerializable
    {
        private SinglyLinkedNode<T> _head = new SinglyLinkedNode<T>(default(T));
        public LLStack()
        {
        }
        public LLStack(IEnumerable<T> collection)
        {
            foreach(T item in collection)
                Push(item);
        }
        private LLStack(SerializationInfo info, StreamingContext context)
            :this((T[])info.GetValue("arr", typeof(T[]))){}
        [SecurityCritical]
        [SecurityPermission(SecurityAction.Demand, SerializationFormatter = true)]
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("arr", ToArray(), typeof(T[]));
        }
        public void Push(T item)
        {
            SinglyLinkedNode<T> node = new SinglyLinkedNode<T>(item);
            SinglyLinkedNode<T> next = node.Next = _head.Next;
            for(;;)
            {
                SinglyLinkedNode<T> oldNext = Interlocked.CompareExchange(ref _head.Next, node, next);
                if(oldNext == next)
                    return;
                next = node.Next = oldNext;
            }
        }
        public void PushRange(IEnumerable<T> items)
        {
            if(items == null)
                throw new ArgumentNullException();
            using(IEnumerator<T> en = items.GetEnumerator())
            {
                if(!en.MoveNext())
                    return;
                SinglyLinkedNode<T> start = new SinglyLinkedNode<T>(en.Current);
                SinglyLinkedNode<T> end = start;
                while(en.MoveNext())
                {
                    SinglyLinkedNode<T> newNode = new SinglyLinkedNode<T>(en.Current);
                    end.Next = newNode;
                    end = newNode;
                }
                SinglyLinkedNode<T> next = end.Next = _head.Next;
                for(;;)
                {
                    SinglyLinkedNode<T> oldNext = Interlocked.CompareExchange(ref _head.Next, start, next);
                    if(oldNext == next)
                        return;
                    next = end.Next = oldNext;
                }
            }
        }
        public bool TryPop(out T item)
        {
            for(;;)
            {
                SinglyLinkedNode<T> node = _head.Next;
                if(node == null)
                {
                    item = default(T);
                    return false;
                }
                SinglyLinkedNode<T> next = node.Next;
                SinglyLinkedNode<T> oldNext = Interlocked.CompareExchange(ref _head.Next, next, node);
                if(oldNext == next)
                {
                    item = node.Item;
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
            get { return _head.Next == null; }
        }
        public class AtPopEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private SinglyLinkedNode<T> _node = new SinglyLinkedNode<T>(default(T));
            internal AtPopEnumerator(LLStack<T> stack)
            {
                SinglyLinkedNode<T> node = stack._head.Next;
                for(;;)
                {
                    SinglyLinkedNode<T> oldNext = Interlocked.CompareExchange(ref stack._head.Next, null, node);
                    if(oldNext == node)
                    {
                        _node.Next = node;
                        return;
                    }
                    node = oldNext;
                }
            }
            public AtPopEnumerator GetEnumerator()
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
                //nop
            }
            public bool MoveNext()
            {
                SinglyLinkedNode<T> next = _node.Next;
                if(next == null)
                    return false;
                _node = next;
                return true;
            }
            void IEnumerator.Reset()
            {
                throw new NotSupportedException();
            }
        }
        public class PopEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private readonly LLStack<T> _stack;
            private T _current;
            internal PopEnumerator(LLStack<T> stack)
            {
                _stack = stack;
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
                return _stack.TryPop(out _current);
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            public void Reset()
            {
                //nop
            }
            public PopEnumerator GetEnumerator()
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
            private readonly LLStack<T> _stack;
            private SinglyLinkedNode<T> _node;
            internal Enumerator(LLStack<T> stack)
            {
                _node = (_stack = stack)._head;
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
                SinglyLinkedNode<T> next = _node.Next;
                if(next == null)
                    return false;
                _node = next;
                return true;
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            public void Reset()
            {
                _node = _stack._head;
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
        public PopEnumerator PopAll()
        {
            return new PopEnumerator(this);
        }
        public AtPopEnumerator AtomicPopAll()
        {
            return new AtPopEnumerator(this);
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
            List<T> list = new List<T>();
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                list.Add(en.Current);
            return list;
        }
        public List<T> PopToList()
        {
            List<T> list = new List<T>();
            AtPopEnumerator pe = new AtPopEnumerator(this);
            while(pe.MoveNext())
                list.Add(pe.Current);
            return list;
        }
        public LLStack<T> Clone()
        {
            return new LLStack<T>(this);
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }
        void ICollection<T>.Add(T item)
        {
            Push(item);
        }
        public void Clear()
        {
            _head.Next = null;
        }
        public bool Contains(T item, IEqualityComparer<T> comparer)
        {
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                if(comparer.Equals(item, en.Current))
                    return true;
            return false;
        }
        public bool Contains(T item)
        {
            return Contains(item, EqualityComparer<T>.Default);
        }
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
            Push(item);
            return true;
        }
        bool IProducerConsumerCollection<T>.TryTake(out T item)
        {
            return TryPop(out item);
        }
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
}
