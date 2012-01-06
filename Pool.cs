// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

// A compiled binary is available from <http://hackcraft.github.com/Ariadne/> which if
// unmodified, may be used without restriction. (This dual-licensing is to provide a clear
// answer to the question of whether using the library in an application counts as creating
// a derivative work).

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;

namespace Ariadne.Collections
{
    /// <summary>A thread-safe pool of objects.</summary>
    /// <remarks>
    /// <para>Pooling objects can be beneficial if creating them carries expense beyond that of allocation (e.g. they hold a
    /// handle to an expensive resource) or if they adapt while being used (e.g. XmlNameTables), and if the object itself
    /// is not thread-safe.</para>
    /// <para>Any object that implements <see cref="IProducerConsumerCollection&lt;T>"/> can be used as the underlying store,
    /// with the thread-safety coming from the thread-safety of that object. If none is passed to the constructor,
    /// a <see cref="LLQueue&lt;T>"/> is used.</para>
    /// <para>The pool does not ensure that the same object is not put in the pool twice. A backing store can be selected
    /// to ensure this (e.g. <see cref="LockFreeSet&lt;T>"/>) but this will not guard against the set of items both currently
    /// pooled and currently in use, containing the same item twice.</para>
    /// </remarks>
    /// <typeparam name="T">The type of the objects in the pool.</typeparam>
    /// <threadsafety static="true" instance="true">The class is intended for cases where it would be thread-safe for
    /// all calls to instance methods, but this thread-safety comes from the underlying <see cref="IProducerConsumerCollection&lt;T>"/>
    /// implementation, and will not be thread safe if it's implementations of <see cref="IProducerConsumerCollection&lt;T>.TryTake"/>
    /// and <see cref="IProducerConsumerCollection&lt;T>.TryAdd"/> are not thread-safe.</threadsafety>
    public class Pool<T>
    {
        private IProducerConsumerCollection<T> _store;
        private Func<T> _factory;
        private int _max;
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="store">The <see cref="IProducerConsumerCollection&lt;T>"/> to use as a backing store to the pool.</param>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <param name="prefill">The pool will be prefilled with this many calls to <paramref name="factory"/> as per calling <see cref="Fill"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> was null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> was less than one or <paramref name="prefill"/> is less than zero.</exception>
        /// <exception cref="ArgumentException">Both <paramref name="factory"/> is null and <paramref name="prefill"/> is greater than zero.</exception>
        public Pool(IProducerConsumerCollection<T> store, Func<T> factory, int max, int prefill)
        {
            if(store == null)
                throw new ArgumentNullException("store");
            if(max < 1)
                throw new ArgumentOutOfRangeException("max");
            if(prefill < 0)
                throw new ArgumentOutOfRangeException("prefill");
            if(factory == null && prefill != 0)
                throw new ArgumentException();
            _store = store;
            _factory = factory;
            _max = max;
            if(prefill != 0)
                Fill(prefill);
        }
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="store">The <see cref="IProducerConsumerCollection&lt;T>"/> to use as a backing store to the pool.</param>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> was null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> was less than one.</exception>
        public Pool(IProducerConsumerCollection<T> store, Func<T> factory, int max)
            :this(store, factory, max, 0){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="store">The <see cref="IProducerConsumerCollection&lt;T>"/> to use as a backing store to the pool.</param>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> was null.</exception>
        public Pool(IProducerConsumerCollection<T> store, Func<T> factory)
            :this(store, factory, int.MaxValue){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="store">The <see cref="IProducerConsumerCollection&lt;T>"/> to use as a backing store to the pool.</param>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> was null.</exception>
        public Pool(IProducerConsumerCollection<T> store, int max)
            :this(store, null, max){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="store">The <see cref="IProducerConsumerCollection&lt;T>"/> to use as a backing store to the pool.</param>
        /// <exception cref="ArgumentNullException"><paramref name="store"/> was null.</exception>
        public Pool(IProducerConsumerCollection<T> store)
            :this(store, null, int.MaxValue){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <param name="prefill">The pool will be prefilled with this many calls to <paramref name="factory"/> as per calling <see cref="Fill"/></param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> was less than one or <paramref name="prefill"/> is less than zero.</exception>
        /// <exception cref="ArgumentException">Both <paramref name="factory"/> is null and <paramref name="prefill"/> is greater than zero.</exception>
        public Pool(Func<T> factory, int max, int prefill)
            :this(new LLQueue<T>(), factory, max, prefill){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> was less than one.</exception>
        public Pool(Func<T> factory, int max)
            :this(factory, max, 0){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="factory">The default factory to create new items as needed. It can be null, but in this case
        /// the overload of <see cref="Get()"/> that doesn't take a factory as a parameter will throw <see cref="InvalidOperationException"/>.</param>
        public Pool(Func<T> factory)
            :this(factory, int.MaxValue){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        /// <param name="max">A maximum size for the pool. If <see cref="int.MaxValue"/> is passed, the
        /// maximum is ignored.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> was less than one.</exception>
        public Pool(int max)
            :this((Func<T>)null, max){}
        /// <summary>Creates a new <see cref="Pool&lt;T>"/> object.</summary>
        public Pool()
            :this(int.MaxValue){}
        /// <summary>Calls the default factory <paramref name="count"/> times, and adds the results to the pool.</summary>
        /// <param name="count">The number of objects to add.</param>
        /// <remarks>If the underlying store rejects some of the items, this is ignored.</remarks>
        public void Fill(int count)
        {
            if(count < 1)
                throw new ArgumentOutOfRangeException("count");
            if(_factory == null)
                throw new InvalidOperationException();
            while(count-- != 0)
                Store(_factory());
        }
        /// <summary>Attempts to obtain an object from the pool.</summary>
        /// <param name="item">The item obtained if successful, or the default value for <c>T</c> otherwise.</param>
        /// <returns>True if the method succeeds, false if there were </returns>
        public bool TryGet(out T item)
        {
            if(_store.TryTake(out item))
                return true;
            if(_factory == null)
                return false;
            item = _factory();
            return true;
        }
        /// <summary>Obtains an object from the pool, or creates one with the default factory.</summary>
        /// <returns>The object obtained or created</returns>
        /// <exception cref="InvalidOperationException">The pool was empty, and no default factory
        /// was passed to the pool’s constructor.</exception>
        public T Get()
        {
            T ret;
            if(TryGet(out ret))
                return ret;
            else
                throw new InvalidOperationException();
        }
        /// <summary>Obtains an object from the pool, or creates one with the factory passed.</summary>
        /// <param name="factory">A <see cref="Func&lt;T>"/> to create a new object, if the pool is empty.</param>
        /// <returns>The object obtained or created.</returns>
        public T Get(Func<T> factory)
        {
            if(factory == null)
                throw new ArgumentNullException("factory");
            T ret;
            return _store.TryTake(out ret) ? ret : factory();
        }
        /// <summary>Stores an object in the pool.</summary>
        /// <param name="item">The object to store.</param>
        /// <returns>True if the object was stored, false if the maximum size of the pool is not
        /// set to <see cref="int.MaxValue"/> and the pool's size is already of maximum size.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="item"/> is null.</exception>
        /// <remarks>
        /// <para>The size of the pool is tested by calling the underlying store’s
        /// <see cref="ICollection.Count"/> property, and is hence
        /// as performant as that property.</para>
        /// <para>Multiple simultaneous calls can result in the queue exceeding its maximum size. It is assumed
        /// that such races, while perhaps sub-optimal, are not harmful.</para>
        /// </remarks>
        public bool Store(T item)
        {
            if(typeof(T).IsClass && ReferenceEquals(item, null))
                throw new ArgumentNullException("item");
            return (_max == int.MaxValue || Count < _max) && _store.TryAdd(item);
        }
        /// <summary>Returns the number of items in the pool.</summary>
        /// <remarks>The size of the pool is tested by calling the underlying store’s
        /// <see cref="ICollection.Count"/> property, and is hence
        /// as performant as that property.</remarks>
        public int Count
        {
            get { return _store.Count; }
        }
        /// <summary>Keeps track of an object from the pool, and returns it to the pool upon disposal.</summary>
        /// <remarks>
        /// <para>This class is intended to make it easy to ensure that objects are returned to the pool as per
        /// the example.</para>
        /// <para>The intention is that the object should be returned to the pool, even when the code using
        /// it throws an exception. It should <strong>not</strong> be used if this could corrupt the
        /// state of the object.</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// using(var poolHandle = pool.GetHandle(() => CreateObject()))
        /// {
        ///    var obj = poolHandle.Object;
        ///     /*
        ///      *
        ///      * use obj here.
        ///      * 
        ///      */
        /// } // obj returned to the pool here.
        /// </code>
        /// </example>
        /// <threadsafety static="true" instance="false">This class is not thread-safe in itself. It is designed
        /// to allow a single thread a means to ensure it will always return an object to the pool, and as
        /// such should only be disposed by a single thread.</threadsafety>
        /// <tocexclude/>
        public class Handle : IDisposable
        {
            private readonly Pool<T> _pool;
            private T _object;
            internal Handle(Pool<T> pool)
            {
                _object = (_pool = pool).Get();
            }
            internal Handle(Pool<T> pool, Func<T> factory)
            {
                _object = (_pool = pool).Get(factory);
            }
            /// <summary>Returns the object that the handle holds.</summary>
            public T Object
            {
                get { return _object; }
            }
            /// <summary>Places the object back in the pool. After this is called,
            /// <see cref="Object"/> will return the default value of <c>T</c>
            /// (<c>null</c> for reference types).</summary>
            public void Dispose()
            {
                if(_object != null)
                {
                    _pool.Store(_object);
                    //make duplicate calls safe.
                    _object = default(T);
                }
            }
        }
        /// <overloads>
        /// <summary>Returns a <see cref="Handle"/> that will obtain an object from the
        /// pool or created by a factory, and return it to the pool upon disposal.</summary>
        /// <returns>A <see cref="Handle"/> with an object from the pool.</returns>
        /// <example>
        /// <code>
        /// using(var poolHandle = pool.GetHandle())
        /// {
        ///    var obj = poolHandle.Object;
        ///     /*
        ///      *
        ///      * use obj here.
        ///      * 
        ///      */
        /// } // obj returned to the pool here.
        /// </code>
        /// </example>
        /// </overloads>
        /// <summary>Returns a <see cref="Handle"/> that will obtain an object from the
        /// pool or create it with the pool’s default factory, and return it to the pool upon disposal.</summary>
        /// <returns>A <see cref="Handle"/> with an object from the pool.</returns>
        /// <example>
        /// <code>
        /// using(var poolHandle = pool.GetHandle())
        /// {
        ///    var obj = poolHandle.Object;
        ///     /*
        ///      *
        ///      * use obj here.
        ///      * 
        ///      */
        /// } // obj returned to the pool here.
        /// </code>
        /// </example>
        /// <exception cref="InvalidOperationException">The pool did not have a default factory defined when
        /// it was constructed.</exception>
        public Handle GetHandle()
        {
            if(_factory == null)
                throw new InvalidOperationException();
            return new Handle(this);
        }
        /// <summary>Returns a <see cref="Handle"/> that will obtain an object from the
        /// pool or create it with the factory passed to it, and return it to the pool upon disposal.</summary>
        /// <param name="factory">The <see cref="Func&lt;T>"/> to create an object, should the pool be empty.</param>
        /// <returns>A <see cref="Handle"/> with an object from the pool.</returns>
        /// <example>
        /// <code>
        /// using(var poolHandle = pool.GetHandle(() => CreateObject()))
        /// {
        ///    var obj = poolHandle.Object;
        ///     /*
        ///      *
        ///      * use obj here.
        ///      * 
        ///      */
        /// } // obj returned to the pool here.
        /// </code>
        /// </example>
        public Handle GetHandle(Func<T> factory)
        {
            if(factory == null)
                throw new ArgumentNullException("factory");
            return new Handle(this, factory);
        }
    }
}
