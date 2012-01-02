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
    
    /// <summary>A lock-free type-safe stack. This class is included mainly for competion, to allow for
    /// adoption to framework versions prior to the introduction of <see cref="ConcurrentStack&lt;T>"/>
    /// and for use as the basis of other algorithms in this library. It does however also offer
    /// some other functionality.</summary>
    [Serializable]
    public class LLStack<T> : ICollection<T>, IProducerConsumerCollection<T>, ICloneable, ISerializable
    {
        private SinglyLinkedNode<T> _head = new SinglyLinkedNode<T>(default(T));
        /// <summary>Creates a new <see cref="LLStack&lt;T>"/></summary>
        public LLStack()
        {
        }
        /// <summary>Creates a new <see cref="LLStack&lt;T>"/> filled from the collection passed to it.</summary>
        /// <param name="collection">An <see cref="IEnumerable&lt;T>"/> that the stack will be filled from on construction.</param>
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
        /// <summary>Adds an item to the top of the stack.</summary>
        /// <param name="item">The item to add.</param>
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
        /// <summary>Pushes a collection of items to the top of the stack as a single atomic operation.</summary>
        /// <param name="items">The items to push onto the stack.</param>
        /// <exception cref="ArgumentNullException"/>The collection was null.
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
        /// <summary>Attempts to pop an item from the top of the stack.</summary>
        /// <param name="item">The item removed from the stack.</param>
        /// <returns>True if the method succeeds, false if the stack was empty.</returns>
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
        /// <summary>Attempts to retrieve the item that is currently at the top of the stack, without removing it.</summary>
        /// <param name="item">The item at the top of the stack.</param>
        /// <returns>True if the method succeeds, false if the stack was empty.</returns>
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
        /// <summary>Tests whether the stack has no items.</summary>
        /// <remarks>The operation is atomic, but may be stale by the time it returns.</remarks>
        public bool IsEmpty
        {
            get { return _head.Next == null; }
        }
        /// <summary>An enumeration &amp; enumerator of items that were removed from the stack as an atomic operation.</summary>
        /// <remarks><see cref="AtomicPopAll"/> for more information.</remarks>
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
            /// <summary>Returns the enumeration itself, as it is also it’s own enumerator.</summary>
            /// <returns>The enumeration itself.</returns>
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
        /// <summary>An enumeration &amp; enumerator of items that are removed from the stack as the enumeration is processed</summary>
        /// <remarks><see cref="PopAll"/> for more information.</remarks>
        public class PopEnumerator : IEnumerable<T>, IEnumerator<T>
        {
            private readonly LLStack<T> _stack;
            private T _current;
            internal PopEnumerator(LLStack<T> stack)
            {
                _stack = stack;
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
            /// <summary>Moves to the next item in the enumeration (removing it from the stack)</summary>
            /// <returns>True if the method succeeds, false if the stack was empty</returns>
            /// <remarks>Since the class refers to the live state of the stack, after returning false
            /// it may return true on a subsequent call, if items were added in the meantime.</remarks>
            public bool MoveNext()
            {
                return _stack.TryPop(out _current);
            }
            void IDisposable.Dispose()
            {
                //nop
            }
            /// <summary>Resets the enumeration.</summary>
            /// <remarks>Since the class refers to the live state of the stack, this is a non-operation
            /// as the enumeration is always attempting to pop from the front of the stack.</remarks>
            public void Reset()
            {
                //nop
            }
            /// <summary>Returns the enumeration itself, as it is also it’s own enumerator.</summary>
            /// <returns>The enumeration itself.</returns>
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
        /// <summary>An enumerator that enumerates through the stack, without removing them.</summary>
        /// <remarks>The enumerator is created based on the current top of the stack and continues
        /// until it reaches what is then the end. It may therefore on the one hand return items that
        /// have already been popped, and on the other never reach an end should new items be added
        /// frequently enough.</remarks>
        public class Enumerator : IEnumerator<T>
        {
            private readonly LLStack<T> _stack;
            private SinglyLinkedNode<T> _node;
            internal Enumerator(LLStack<T> stack)
            {
                _node = (_stack = stack)._head;
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
            /// <returns>True if an item is found, false if it reaches the end of the stack.</returns>
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
            /// <summary>Resets the enumeration to the current top of the stack.</summary>
            public void Reset()
            {
                _node = _stack._head;
            }
        }
        /// <summary>Returns an object that enumerates the stack without removing items.</summary>
        /// <returns>An <see cref="Enumerator"/> that starts with the current top of the stack.</returns>
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
        /// <summary>Returns an enumerator that removes items from the stack as it returns them.</summary>
        /// <returns>A <see cref="PopEnumerator"/></returns>
        /// <remarks>The operation is not atomic and will interleave with other pop operations, and can
        /// return items pushed after the method was called. Use <see cref="AtomicPopAll"/> if you
        /// want to clear the stack as a single atomic operation.</remarks>
        public PopEnumerator PopAll()
        {
            return new PopEnumerator(this);
        }
        /// <summary>Clears the stack as an atomic operation, and returns an enumeration of the items so-removed.</summary>
        /// <returns>A <see cref="AtPopEnumerator"/> that enumerates through the items removed.</returns>
        public AtPopEnumerator AtomicPopAll()
        {
            return new AtPopEnumerator(this);
        }
        /// <summary>Returns the count of the stack.</summary>
        /// <remarks>The operation is O(n), and may be stale by the time it returns.</remarks>
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
        /// <summary>Returns a <see cref="List&lt;T>"/> of the current items in the stack without removing them.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the current items in the stack.</returns>
        public List<T> ToList()
        {
            List<T> list = new List<T>();
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                list.Add(en.Current);
            return list;
        }
        /// <summary>Clears the stack as an atomic operation, and returns a <see cref="List&lt;T>"/> of the items removed.</summary>
        /// <returns>A <see cref="List&lt;T>"/> of the items removed.</returns>
        public List<T> PopToList()
        {
            List<T> list = new List<T>();
            AtPopEnumerator pe = new AtPopEnumerator(this);
            while(pe.MoveNext())
                list.Add(pe.Current);
            return list;
        }
        /// <summary>Creates a new stack with the same items as this one.</summary>
        /// <returns>A new <see cref="LLStack&lt;T>"/>.</returns>
        public LLStack<T> Clone()
        {
            return new LLStack<T>(this);
        }
        object ICloneable.Clone()
        {
            return Clone();
        }
        /// <summary>Clears the stack as a single atomic operation, and returns a stack with the same contents as those removed.</summary>
        /// <returns>The new stack.</returns>
        public LLStack<T> Transfer()
        {
            return new LLStack<T>(AtomicPopAll());
        }
        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }
        void ICollection<T>.Add(T item)
        {
            Push(item);
        }
        /// <summary>Clears the stack as a single atomic operation.</summary>
        public void Clear()
        {
            _head.Next = null;
        }
        /// <summary>Examines the stack for the presence of an item.</summary>
        /// <param name="item">The item to search for.</param>
        /// <param name="comparer">An <see cref="IEqualityComparer&lt;T>"/> to use to compare
        /// the item with those in the collections.</param>
        /// <returns>True if the item was found, false otherwise.</returns>
        public bool Contains(T item, IEqualityComparer<T> comparer)
        {
            Enumerator en = new Enumerator(this);
            while(en.MoveNext())
                if(comparer.Equals(item, en.Current))
                    return true;
            return false;
        }
        /// <summary>Examines the stack for the presence of an item.</summary>
        /// <param name="item">The item to search for.</param>
        /// <returns>True if the item was found, false otherwise.</returns>
        public bool Contains(T item)
        {
            return Contains(item, EqualityComparer<T>.Default);
        }
        /// <summary>Copies the contents of the stack to an array.</summary>
        /// <param name="array">The array to copy to.</param>
        /// <param name="arrayIndex">The index within the array to start copying from</param>
        /// <exception cref="System.ArgumentNullException"/>The array was null.
        /// <exception cref="System.ArgumentOutOfRangeException"/>The array index was less than zero.
        /// <exception cref="System.ArgumentException"/>The number of items in the collection was
        /// too great to copy into the array at the index given.
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
        /// <summary>Returns an array of the current items in the stack without removing them.</summary>
        /// <returns>The array of the current items in the stack.</returns>
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
