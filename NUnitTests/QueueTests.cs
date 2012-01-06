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
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using Ariadne.Collections;
using NUnit.Framework;

namespace Ariadne.NUnitTests.QueueTests
{
    [TestFixture]
    public class SingleThreaded
    {
        [Test]
        public void SimpleAddAndRemove()
        {
            var queue = new LLQueue<int>();
            for(int i = 0; i != 10; ++i)
                queue.Enqueue(i);
            int cur = 0;
            int res;
            int peek;
            queue.TryPeek(out peek);
            Assert.AreEqual(0, peek);
            while(queue.TryDequeue(out res))
            {
                Assert.AreEqual(cur++, res);
                if(queue.TryPeek(out peek))
                {
                    Assert.AreEqual(cur, peek);
                    Assert.IsFalse(queue.IsEmpty);
                }
                else
                    Assert.IsTrue(queue.IsEmpty);
            }
        }
        [Test]
        public void Enumerate()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            int cur = 0;
            foreach(int i in queue)
                Assert.AreEqual(cur++, i);
            Assert.AreEqual(100, queue.Count);
            cur = 0;
            foreach(int i in queue.DequeueAll())
            {
                Assert.AreEqual(cur++, i);
                Assert.AreEqual(100 - cur, queue.Count);
            }
            Assert.AreEqual(0, queue.Count);
            queue.EnqueueRange(Enumerable.Range(0, 100));
            Assert.AreEqual(100, queue.Count);
            cur = 0;
            foreach(int i in queue.AtomicDequeueAll())
            {
                Assert.AreEqual(cur++, i);
                Assert.AreEqual(0, queue.Count);
            }
        }
        [Test]
        public void Serialisation()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            using(MemoryStream ms = new MemoryStream())
            {
                new BinaryFormatter().Serialize(ms, queue);
                ms.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                Assert.IsTrue(queue.ToList().SequenceEqual((LLQueue<int>)new BinaryFormatter().Deserialize(ms)));
            }
        }
        private class FinalisationNoter
        {
            public static int FinalisationCount;
            ~FinalisationNoter()
            {
                FinalisationCount++;
            }
        }
        [Test]
        public void ClearLast()
        {
            var queue = new LLQueue<FinalisationNoter>();
            queue.Enqueue(new FinalisationNoter());
            queue.Enqueue(new FinalisationNoter());
            queue.DequeueToList();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.AreEqual(1, FinalisationNoter.FinalisationCount);
            queue.ClearLastItem();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.AreEqual(2, FinalisationNoter.FinalisationCount);
        }
        [Test]
        public void CountMax()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            Assert.AreEqual(100, queue.CountUntil(200));
            Assert.AreEqual(100, queue.CountUntil(100));
            Assert.AreEqual(50, queue.CountUntil(50));
            Assert.AreEqual(0, queue.CountUntil(0));
            queue.Clear();
            Assert.IsTrue(queue.IsEmpty);
        }
        [Test]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void CountInvalid()
        {
            new LLQueue<int>().CountUntil(-3);
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullRange()
        {
            new LLQueue<int>().EnqueueRange(null);
        }
        [Test]
        [ExpectedException(typeof(ArgumentNullException))]
        public void NullConstructor()
        {
            new LLQueue<int>(null);
        }
        [Test]
        public void ICloneable()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            var clone = (LLQueue<int>)((ICloneable)queue).Clone();
            Assert.IsTrue(queue.SequenceEqual(clone));
        }
        [Test]
        public void Transfer()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            var trans = queue.Transfer();
            Assert.IsTrue(queue.IsEmpty);
            int cmp = 0;
            foreach(int i in queue.ToArray())
                Assert.AreEqual(cmp++, i);
        }
        [Test]
        public void IProducerConsumerCollection()
        {
            var queue = (IProducerConsumerCollection<int>)new LLQueue<int>();
            for(int i = 0; i != 10; ++i)
                queue.TryAdd(i);
            int cur = 0;
            int res;
            while(queue.TryTake(out res))
            {
                Assert.AreEqual(cur++, res);
            }
        }
        [Test]
        public void Contains()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            Assert.IsTrue(queue.Contains(50));
            Assert.IsFalse(queue.Contains(100));
        }
        [Test]
        public void CopyTo()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            var array = new int[150];
            queue.CopyTo(array, 50);
            Assert.IsTrue(array.Skip(50).SequenceEqual(queue));
            array = new int[150];
            ((ICollection)queue).CopyTo(array, 50);
            Assert.IsTrue(array.Skip(50).SequenceEqual(queue));
        }
        [Test]
        public void ICollection()
        {
            ICollection queue = new LLQueue<int>(Enumerable.Range(0, 10));
            Assert.IsFalse(queue.IsSynchronized);
            int cmp = 0;
            foreach(int i in queue)
                Assert.AreEqual(cmp++, i);
        }
        [Test]
        [ExpectedException(typeof(NotSupportedException))]
        public void SyncRootFail()
        {
            ICollection queue = new LLQueue<int>(Enumerable.Range(0, 100));
            object root = queue.SyncRoot;
        }
        [Test]
        public void ICollectionT()
        {
            ICollection<int> queue = new LLQueue<int>();
            Assert.IsFalse(queue.IsReadOnly);
            queue.Add(1);
            foreach(int i in queue)
                Assert.AreEqual(1, i);
        }
        [Test]
        [ExpectedException(typeof(NotSupportedException))]
        public void CantRemove()
        {
            ICollection<int> queue = new LLQueue<int>();
            queue.Remove(93);
        }
        [Test]
        public void ResetEnum()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            var en = queue.GetEnumerator();
            while(en.MoveNext() && en.Current < 50);
            en.Reset();
            int cmp = 0;
            while(en.MoveNext())
                Assert.AreEqual(cmp++, en.Current);
        }
        [Test]
        public void ResetDeEnum()
        {
            var queue = new LLQueue<int>(Enumerable.Range(0, 100));
            var en = queue.DequeueAll();
            while(en.MoveNext() && en.Current < 50);
            en.Reset();
            int cmp = 51;
            while(en.MoveNext())
                Assert.AreEqual(cmp++, en.Current);
        }
        [Test]
        [ExpectedException(typeof(NotSupportedException))]
        public void ResetAtDeEnum()
        {
            ((IEnumerator)new LLQueue<int>().AtomicDequeueAll()).Reset();
        }
    }
    [TestFixture]
    public class MultiThreaded
    {
        private static int CoreCount()
        {
            int coreCount = 0;
			IntPtr affinity = Process.GetCurrentProcess().ProcessorAffinity;
			for(int aff = (int)affinity; aff != 0; aff >>= 1)
				if((aff & 1) != 0)
					coreCount++;
			return coreCount;
        }
        [Test]
        public void MultiAdd()
        {
            int cores = CoreCount();
            var threads = new Thread[cores];
            var queue = new LLQueue<int>();
            for(int i = 0; i != cores; ++i)
            {
                threads[i] = new Thread(obj =>
                                        {
                                            int off = (int)obj;
                                            for(int x = 0; x != 100000; ++x)
                                                queue.Enqueue(x * cores + off);
                                        });
            }
            for(int i = 0; i != cores; ++i)
                threads[i].Start(i);
            for(int i = 0; i != cores; ++i)
                threads[i].Join();
            Dictionary<int, int> dict = new Dictionary<int, int>();
            for(int i = 0; i != cores; ++i)
                dict[i] = -1;
            foreach(int test in queue)
            {
                int bucket = test % cores;
                int last = dict[bucket];
                Assert.IsTrue(test > last);
                dict[bucket] = last;
            }
        }
        [Test]
        public void HalfDequeue()
        {
            int cores = CoreCount();
            if(cores < 2)
                cores = 2;
            var threads = new Thread[cores];
            var queue = new LLQueue<int>();
            var secQueue = new LLQueue<int>();
            for(int i = 0; i != cores; ++i)
            {
                if(i % 2 == 0)
                    threads[i] = new Thread(obj =>
                                            {
                                                int off = (int)obj;
                                                for(int x = 0; x != 100000; ++x)
                                                    queue.Enqueue(x * cores + off);
                                            });
                else
                    threads[i] = new Thread(obj =>
                                            {
                                                foreach(int taken in queue.DequeueAll())
                                                    secQueue.Enqueue(taken);
                                            });
            }
            for(int i = 0; i < cores; i += 2)
                threads[i].Start(i);
            for(int i = 1; i < cores; i += 2)
                threads[i].Start(i);
            for(int i = 0; i != cores; ++i)
                threads[i].Join();
            Dictionary<int, int> dict = new Dictionary<int, int>();
            for(int i = 0; i != cores; ++i)
                dict[i] = -1;
            secQueue.EnqueueRange(queue);
            foreach(int test in secQueue)
            {
                int bucket = test % cores;
                int last = dict[bucket];
                Assert.IsTrue(test > last);
                dict[bucket] = last;
            }
        }
        [Test]
        public void RacingEnumeration()
        {
            int cores = CoreCount();
            if(cores < 2)
                cores = 2;
            var threads = new Thread[cores];
            var queue = new LLQueue<int>();
            bool failed = false;
            for(int i = 0; i != cores; ++i)
            {
                if(i % 2 == 0)
                    threads[i] = new Thread(obj =>
                                            {
                                                int off = (int)obj;
                                                for(int x = 0; x != 100000; ++x)
                                                    queue.Enqueue(x * cores + off);
                                            });
                else
                    threads[i] = new Thread(obj =>
                                            {
                                                int prev = -1;
                                                foreach(int taken in queue)
                                                {
                                                    if(prev >= taken)
                                                        failed = false;
                                                    prev = taken;
                                                }
                                            });
            }
            for(int i = 0; i < cores; i += 2)
                threads[i].Start(i);
            for(int i = 1; i < cores; i += 2)
                threads[i].Start(i);
            for(int i = 0; i != cores; ++i)
                threads[i].Join();
            Assert.IsFalse(failed);
        }
        [Test]
        public void RacingClear()
        {
            int cores = CoreCount();
            if(cores < 2)
                cores = 2;
            var threads = new Thread[cores];
            var queue = new LLQueue<int>();
            int done = 1;
            threads[0] = new Thread(() =>
                                    {
                                        while(done != cores)
                                            for(int i = 0; i != 1000; ++i)
                                                queue.Clear();
                                    });
            for(int i = 1; i != cores; ++i)
            {
                    threads[i] = new Thread(obj =>
                                            {
                                                int off = (int)obj;
                                                for(int x = 0; x != 100000; ++x)
                                                    queue.Enqueue(x * cores + off);
                                                Interlocked.Increment(ref done);
                                            });
            }
            threads[0].Start();
            for(int i = 1; i < cores; ++i)
                threads[i].Start(i);
            threads[0].Join();
            Assert.IsTrue(queue.IsEmpty);
        }
        [Test]
        public void RacingClears()
        {
            int cores = CoreCount();
            if(cores < 2)
                cores = 2;
            var threads = new Thread[cores];
            var queue = new LLQueue<int>();
            int done = 0;
            threads[0] = new Thread(() =>
                                        {
                                            for(int x = 0; x != 1000000; ++x)
                                                queue.Enqueue(x);
                                            Interlocked.Increment(ref done);
                                        });
            for(int i = 1; i != cores; ++i)
            {
                threads[i] = new Thread(() =>
                                        {
                                            while(done == 0)
                                                for(int x = 0; x != 1000; ++x)
                                                    queue.Clear();
                                        });
            }
            for(int i = 0; i < cores; ++i)
                threads[i].Start();
            for(int i = 0; i < cores; ++i)
                threads[i].Join();
            Assert.IsTrue(queue.IsEmpty);
        }
    }
}
