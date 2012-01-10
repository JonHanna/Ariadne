// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections.Concurrent;
using NUnit.Framework;
using Ariadne.Collections;

namespace Ariadne.NUnitTests
{
    [TestFixture]
    public class PoolTests
    {
        [Test]
        public void Handle()
        {
            bool called = false;
            var pool = new Pool<int>(() => {called = true; return 0;}, int.MaxValue, 2);
            Assert.IsTrue(called);
            called = false;
            Assert.AreEqual(2, pool.Count);
            using(var handle = pool.GetHandle())
                Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(2, pool.Count);
            Assert.IsFalse(called);
            while(pool.Count != 0)
                pool.Get();
            using(var handle = pool.GetHandle())
            {
                Assert.IsTrue(called);
                Assert.AreEqual(0, pool.Count);
            }
            Assert.AreEqual(1, pool.Count);
            Assert.AreEqual(0, pool.Get());
            using(var handle = pool.GetHandle(() => 3))
                Assert.AreEqual(3, handle.Object);
        }
        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public void HandleInvalid()
        {
            new Pool<int>().GetHandle();
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void StoreNull()
        {
            new Pool<string>().Store(null);
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullStore()
        {
            new Pool<int>((IProducerConsumerCollection<int>)null);
        }
        [Test]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void InvalidMax()
        {
            new Pool<int>(0);
        }
        [Test]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void InvalidFillCtor()
        {
            new Pool<int>(null, 1, -1);
        }
        [Test]
        [ExpectedException(typeof(ArgumentException))]
        public void FillNullFactory()
        {
            new Pool<int>(null, 1, 1);
        }
        [Test]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void NegativeFill()
        {
            new Pool<int>().Fill(-1);
        }
        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public void InvalidFill()
        {
            new Pool<int>().Fill(34);
        }
        [Test]
        public void CantGet()
        {
            var pool = new Pool<int>();
            pool.Store(3);
            int tmp;
            Assert.IsTrue(pool.TryGet(out tmp));
            Assert.IsFalse(pool.TryGet(out tmp));
        }
        [Test]
        [ExpectedException(typeof(InvalidOperationException))]
        public void CantGetEx()
        {
            var pool = new Pool<int>();
            pool.Store(3);
            pool.Get();
            pool.Get();
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullFactory()
        {
            new Pool<int>().Get(null);
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullFactoryHandle()
        {
            new Pool<int>().GetHandle(null);
        }
        [Test]
        public void DefaultFactory()
        {
            bool called = false;
            var pool = new Pool<int>(() => {
                                         called = true;
                                         return 3;
                                     });
            pool.Store(2);
            Assert.AreEqual(2, pool.Get());
            Assert.IsFalse(called);
            Assert.AreEqual(3, pool.Get());
            Assert.IsTrue(called);
        }
        [Test]
        public void DifferentStore()
        {
            var pool = new Pool<int>(new ThreadSafeSet<int>());
            pool.Store(3);
            pool.Store(3);
            pool.Store(3);
            Assert.AreEqual(1, pool.Count);
        }
        [Test]
        public void Max()
        {
            var pool = new Pool<int>(new LLStack<int>(), 3);
            for(int i = 0; i != 10; ++i)
                pool.Store(i);
            Assert.AreEqual(3, pool.Count);
        }
        [Test]
        public void StoreAndFact()
        {
            var pool = new Pool<int>(new ThreadSafeSet<int>(), () => 4);
            using(var hand = pool.GetHandle())
                using(var hand2 = pool.GetHandle()){}
            Assert.AreEqual(1, pool.Count);
        }
    }
}
