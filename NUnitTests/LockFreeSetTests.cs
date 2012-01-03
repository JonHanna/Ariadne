// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;

using NUnit.Framework;

namespace Ariadne.NUnitTests
{
    [TestFixture]
    public class LockFreeSetTests
    {
    	protected const int SourceDataLen = 131072;
    	protected static string[] SourceData = new string[SourceDataLen];
    	private HashSet<string> FilledStringCompareSet;
    	[TestFixtureSetUpAttribute]
        public void FillSourceData()
        {
        	HashSet<string> hs = new HashSet<string>();
            Random rnd = new Random(1);
            while(hs.Count < SourceDataLen)
            {
                int len = rnd.Next(1, 40);
                char[] chars = new char[len];
                while(len-- != 0)
                    chars[len] = (char)rnd.Next(0, char.MaxValue);
                hs.Add(new string(chars));
            }
            int idx = 0;
            foreach(string str in hs)
            	SourceData[idx++] = str;
            FilledStringCompareSet = hs;
        }
        private LockFreeSet<string> FilledStringSet()
        {
            var hs = new LockFreeSet<string>();
    		for(int i = 0; i != SourceDataLen; ++i)
    		    hs.Add(SourceData[i]);
    		return hs;
        }
    	[Test]
    	public void LoadSome()
    	{
    		Assert.AreEqual(FilledStringSet().Count, SourceDataLen);
    	}
    	private IEnumerable<string> EnumerateData()
    	{
    	    foreach(string str in SourceData)
    	        yield return str;
    	}
    	[Test]
    	public void CreateFromIEnum()
    	{
    	    var setIE = new LockFreeSet<string>(EnumerateData());
    	    var setIL = new LockFreeSet<string>(SourceData);
    	    var hs = FilledStringSet();
    	    Assert.AreEqual(setIE.Count, SourceDataLen);
    	    Assert.AreEqual(setIL.Count, SourceDataLen);
    	    foreach(string str in SourceData)
    	    {
    	        Assert.IsTrue(setIE.Contains(str));
    	        Assert.IsTrue(setIL.Contains(str));
    	    }
    	}
    	[Test]
    	public void Clear()
    	{
    	    var hs = FilledStringSet();
    	    hs.Clear();
    	    Assert.AreEqual(0, hs.Count);
    	}
    	[Test]
    	public void RemoveOneByOne()
    	{
    	    var hs = FilledStringSet();
    	    int count = SourceDataLen;
    	    while(count != 0)
    	    {
    	        Assert.AreEqual(count, hs.Count);
    	        --count;
    	        Assert.IsTrue(hs.Remove(SourceData[count]));
    	    }
    	    Assert.AreEqual(0, hs.Count);
    	}
    	[Test]
    	public void RemoveOneByOneSelfEnum()
    	{
    	    var hs = FilledStringSet();
    	    int count = SourceDataLen;
    	    foreach(string item in hs)
    	    {
    	        Assert.AreEqual(count, hs.Count);
    	        --count;
    	        hs.Remove(item);
    	    }
    	}
    	[Test]
    	public void BackAgain()
    	{
    	    var hs = FilledStringSet();
    	    int count = SourceDataLen;
    	    while(count != 0)
    	    {
    	        --count;
    	        hs.Remove(SourceData[count]);
    	    }
    	    for(int i = 0; i != SourceDataLen; ++i)
    	        hs.Add(SourceData[i]);
    	    Assert.AreEqual(SourceDataLen, hs.Count);
    	}
    	[Test]
    	public void BackAgainDiff()
    	{
    	    var hs = new LockFreeSet<string>();
    	    for(int i = 0; i < SourceDataLen; i += 2)
    	        hs.Add(SourceData[i]);
    	    int count = SourceDataLen / 2;
    	    while(count != 0)
    	    {
    	        --count;
    	        hs.Remove(SourceData[count * 2]);
    	    }
    	    for(int i = 1; i < SourceDataLen; i += 2)
    	        hs.Add(SourceData[i]);
    	    Assert.AreEqual(SourceDataLen / 2, hs.Count);
    	}
    	[Test]
    	public void RemoveHalf()
    	{
    	    var hs = FilledStringSet();
    	    for(int i = 0; i != SourceDataLen; i += 2)
    	        hs.Remove(SourceData[i]);
    	    for(int i = 0; i != SourceDataLen; i += 2)
    	        Assert.IsFalse(hs.Contains(SourceData[i]));
    	    for(int i = 1; i < SourceDataLen; i += 2)
    	        Assert.IsTrue(hs.Contains(SourceData[i]));
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentOutOfRangeException))]
    	public void NegativeCapacity()
    	{
    		var dict = new LockFreeSet<decimal>(-1);
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentOutOfRangeException))]
    	public void ExcessiveCapacity()
    	{
    		var dict = new LockFreeSet<decimal>(((int.MaxValue >> 1) + 2));
    	}
    	[Test]
    	public void EqualityComparer()
    	{
    		var hs = new LockFreeSet<string>(StringComparer.InvariantCultureIgnoreCase);
    		hs.Add("Weißbier");
    		Assert.IsTrue(hs.Contains("WEISSBIER"));
    		Assert.IsTrue(hs.Contains("weissbier"));
    		hs.Add("Δίος");
    		Assert.AreEqual(2, hs.Count);
    		Assert.IsTrue(hs.Contains("ΔΊΟΣ"));
    		Assert.IsTrue(hs.Contains("δίος"));
    		Assert.IsFalse(hs.Contains("δίοςδίος"));
    		Assert.IsFalse(hs.Contains("Eggplant"));
    		Assert.IsFalse(hs.Remove("aubergine"));
    		Assert.IsTrue(hs.Remove("Δίος"));
    		Assert.IsTrue(hs.Remove("WEISSBIER"));
    		Assert.IsFalse(hs.Contains("WEISSBIER"));
    		Assert.IsFalse(hs.Remove("WEISSBIER"));
    		Assert.AreEqual(hs.Count, 0);
    	}
    	[Test]
    	public void ConstantReturns()
    	{
    		var hs = new LockFreeSet<int>();
    		Assert.IsFalse(((ICollection<int>)hs).IsReadOnly);
    	}
    	private bool EqualSets<T>(ISet<T> x, ISet<T> y)
    	{
    	    if(x.Count != y.Count)
    	        return false;
    	    foreach(T item in x)
    	        if(!y.Contains(item))
    	            return false;
    	    return true;
    	}
    	[Test]
    	public void Snapshots()
    	{
    	    var hs = FilledStringSet();
    	    var snap = hs.Clone();
    	    var st = hs.ToHashSet();
    	    Assert.IsTrue(EqualSets(hs, snap));
    	    Assert.IsTrue(EqualSets(hs, st));
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentNullException))]
    	public void NullCopy()
    	{
    	    FilledStringSet().CopyTo(null, 0);
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentOutOfRangeException))]
    	public void NegCopy()
    	{
    	    FilledStringSet().CopyTo(new string[32], -1);
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentException))]
    	public void SmallCopy()
    	{
    	    FilledStringSet().CopyTo(new string[2], 0);
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentNullException))]
    	public void NullComparer()
    	{
    	    new LockFreeSet<int>((IEqualityComparer<int>)null);
    	}
    	[Test]
    	[ExpectedException(typeof(ArgumentNullException))]
    	public void NullSourceCollection()
    	{
    	    new LockFreeSet<int>((IEnumerable<int>)null);
    	}
    	[Test]
    	public void CopyWithOffset()
    	{
    	    var hs = FilledStringSet();
    	    hs.Add(null);
    	    string[] arr = new string[hs.Count + 5];
    	    hs.CopyTo(arr, 5);
    	    Assert.IsNotNull(arr[arr.Length - 1] ?? arr[arr.Length - 2]);
    	}
    	private class PathologicalEqualityComparer : IEqualityComparer<int>
    	{
            public bool Equals(int x, int y)
            {
                return x == y;
            }
            public int GetHashCode(int obj)
            {
                return 0;
            }
    	}
    	[Test]
    	public void PathologicalHashAlgorithm()
    	{
    	    var hs = new LockFreeSet<int>(Enumerable.Range(0, 1000));
    	    for(int i = 0; i != 1000; ++i)
    	        Assert.IsTrue(hs.Contains(i));
    	}
    	[Test]
    	public void RemoveWhere()
    	{
    	    var hs = new LockFreeSet<int>(Enumerable.Range(0, 1000));
    	    foreach(int i in hs.RemoveWhere(x => x % 2 == 0))
    	        Assert.AreEqual(0, i % 2);
    	    Assert.AreEqual(500, hs.Count);
    	    foreach(int i in hs)
    	        Assert.AreEqual(1, i % 2);
    	    hs.Clear();
    	    hs.AddRange(Enumerable.Range(0, 1000));
    	    Assert.AreEqual(1000, hs.Count);
    	    hs.AddRange(Enumerable.Range(0, 1500));
    	    Assert.AreEqual(1500, hs.Count);
    	}
    	[Test]
    	public void Serialisation()
    	{
    	    //Note that the behaviour of the BinaryFormatter will fix some of the strings
    	    //used in many of these tests, as not being valid Unicode. This is desirable behaviour
    	    //in real code, but would give false negatives to this test.
    	    var hs = new LockFreeSet<string>(Enumerable.Range(0, 10000).Select(i => i.ToString()));
    	    hs.Add(null);
    	    using(MemoryStream ms = new MemoryStream())
    	    {
    	        new BinaryFormatter().Serialize(ms, hs);
    	        ms.Flush();
    	        ms.Seek(0, SeekOrigin.Begin);
    	        Assert.IsTrue(EqualSets(hs, (LockFreeSet<string>)new BinaryFormatter().Deserialize(ms)));
    	    }
    	}
    }
}
