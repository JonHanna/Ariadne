// © 2011–2014 Jon Hanna.
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

using NUnit.Framework;

using Ariadne.Collections;

namespace Ariadne.NUnitTests
{
    namespace DictionaryTests
    {
        [TestFixture]
        public class SingleThreaded
        {
        	protected const int SourceDataLen = 131072;
        	protected static string[] SourceData = new string[SourceDataLen];
        	[TestFixtureSetUpAttribute]
            public void FillSourceData()
            {
            	HashSet<string> hs = new HashSet<string>();
            	//There’s something abhorrent about the way this creates strings with non-characters
            	//and mis-matched surrogates, but it serves the purpose well.
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
            }
            private ThreadSafeDictionary<string, string> FilledStringDict(int lengthToUse = -1)
            {
                if(lengthToUse < 0)
                    lengthToUse = SourceDataLen;
        		var dict = new ThreadSafeDictionary<string, string>();
        		for(int i = 0; i != lengthToUse; i+= 2)
        			dict[SourceData[i]] = SourceData[i + 1];
        		return dict;
            }
            private Dictionary<string, string> FilledStringCompareDict()
            {
        		var dict = new Dictionary<string, string>();
        		for(int i = 0; i != SourceDataLen; i+= 2)
        			dict[SourceData[i]] = SourceData[i + 1];
        		return dict;
            }
            private IEnumerable<KeyValuePair<string, string>> EnumeratePairs()
            {
            	for(int i = 0; i != SourceDataLen; i+= 2)
            		yield return new KeyValuePair<string, string>(SourceData[i], SourceData[i + 1]);
            }
        	[Test]
        	public void LoadSome()
        	{
        		Assert.AreEqual(FilledStringDict().Count, SourceDataLen / 2);
        	}
        	[Test]
        	public void CreateFromIEnum()
        	{
        		var dictIE = new ThreadSafeDictionary<string, string>(EnumeratePairs());
        		var dictIL = new ThreadSafeDictionary<string, string>(EnumeratePairs().ToList());
        		var dict = FilledStringCompareDict();
        		Assert.AreEqual(dict.Count, SourceDataLen / 2);
        		Assert.AreEqual(dict.Count, SourceDataLen / 2);
        		var cmpDict = FilledStringCompareDict();
        		foreach(var kvp in cmpDict)
        			Assert.AreEqual(dictIL[kvp.Key], kvp.Value);
        		foreach(var kvp in cmpDict)
        			Assert.AreEqual(dictIE[kvp.Key], kvp.Value);
        	}
        	[Test]
        	public void Clear()
        	{
        		var dict = FilledStringDict();
        		dict.Clear();
        		Assert.AreEqual(dict.Count,0);
        	}
        	[Test]
        	public void RemoveOneByOne()
        	{
        		var dict = FilledStringDict();
        		int count = SourceDataLen / 2;
        		while(count != 0)
        		{
        			Assert.AreEqual(dict.Count, count);
        			--count;
        			Assert.IsTrue(dict.Remove(SourceData[count * 2]));
        		}
        		Assert.AreEqual(dict.Count, 0);
        	}
        	[Test]
        	public void RemoveOneByOneFromSelfEnum()
        	{
        		var dict = FilledStringDict();
        		int count = SourceDataLen / 2;
    			foreach(string key in dict.Keys)
    			{
    				Assert.AreEqual(dict.Count, count);
    				--count;
    				dict.Remove(key);
    			}
    			Assert.AreEqual(dict.Count, 0);
        	}
        	[Test]
        	public void BackAgain()
        	{
        		var dict = FilledStringDict();
        		int count = SourceDataLen / 2;
        		while(count != 0)
        		{
        			--count;
        			dict.Remove(SourceData[count * 2]);
        		}
    			for(int i = 0; i != SourceDataLen; i+= 2)
    				dict[SourceData[i]] = SourceData[i + 1];
    			Assert.AreEqual(dict.Count, SourceDataLen / 2);
        	}
        	[Test]
        	public void BackAgainDiff()
        	{
        		var dict = FilledStringDict();
        		int count = SourceDataLen / 2;
        		while(count != 0)
        		{
        			--count;
        			dict.Remove(SourceData[count * 2]);
        		}
    			for(int i = 0; i != SourceDataLen; i+= 2)
    				dict[SourceData[i+1]] = SourceData[i];
    			Assert.AreEqual(dict.Count, SourceDataLen / 2);
        	}
        	[Test]
        	public void HasCorrect()
        	{
        		var dict = FilledStringDict();
        		var cmpDict = FilledStringCompareDict();
        		foreach(var kvp in cmpDict)
        			Assert.AreEqual(dict[kvp.Key], kvp.Value);
        		foreach(var kvp in dict)
        			Assert.AreEqual(cmpDict[kvp.Key], kvp.Value);
        	}
        	private void EnumCurrentTest<T>(IEnumerable<T> col)
        	{
        		using(var ienum = col.GetEnumerator())
        		{
        			ienum.MoveNext();
        			Assert.AreEqual(ienum.Current, ((IEnumerator)ienum).Current);
        		}
        	}
        	[Test]
        	public void EnumerationsEquivalent()
        	{
        		var dict = FilledStringDict();
        		Assert.IsTrue(dict.Count == dict.Keys.Count);
        		Assert.IsTrue(dict.Count == dict.Values.Count);
        		Assert.IsTrue(dict.SequenceEqual((IEnumerable<KeyValuePair<string, string>>)dict));
        		Assert.IsTrue(dict.SequenceEqual(((IEnumerable)dict).Cast<KeyValuePair<string, string>>()));
        		Assert.IsTrue(dict.Keys.Zip(dict.Values, (key, pair) => new KeyValuePair<string, string>(key, pair)).SequenceEqual(dict));
        		EnumCurrentTest(dict);
        		EnumCurrentTest(dict.Keys);
        		EnumCurrentTest(dict.Values);
        		Assert.AreEqual(dict.Keys, ((IDictionary<string, string>)dict).Keys);
        		Assert.AreEqual(dict.Values, ((IDictionary<string, string>)dict).Values);
        		KeyValuePair<string, string>[] kvpArr = new KeyValuePair<string, string>[SourceDataLen / 2];
        		string[] kArr = new string[SourceDataLen / 2];
        		string[] vArr = new string[SourceDataLen / 2];
        		dict.CopyTo(kvpArr, 0);
        		dict.Keys.CopyTo(kArr, 0);
        		dict.Values.CopyTo(vArr, 0);
        		Assert.IsTrue(dict.SequenceEqual(kvpArr));
        		Assert.IsTrue(kArr.Zip(vArr, (k, v) => new KeyValuePair<string, string>(k, v)).SequenceEqual(kvpArr));
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentOutOfRangeException))]
        	public void NegativeCapacity()
        	{
        		new ThreadSafeDictionary<int, decimal>(-1);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentOutOfRangeException))]
        	public void ExcessiveCapacity()
        	{
        		new ThreadSafeDictionary<string, bool>(((int.MaxValue >> 1) + 2));
        	}
        	private class RoundedEquality : IEqualityComparer<int>
        	{
    			public bool Equals(int x, int y)
    			{
    				return x / 10 == y / 10;
    			}
    			public int GetHashCode(int obj)
    			{
    				return obj / 10;
    			}
        	}
        	[Test]
        	public void EqualityComparer()
        	{
                if(!StringComparer.InvariantCultureIgnoreCase.Equals("Weißbier", "WEISSBIER"))
                    Assert.Ignore("Bugs in Mono make this test irrelevant. When the bug is fixed, this test will run.");
        		var dict = new ThreadSafeDictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
        		dict.Add("Weißbier", 93);
        		Assert.AreEqual(dict["WEISSBIER"], 93);
        		dict["weissbier"] = 777;
        		Assert.AreEqual(dict["Weißbier"], 777);
        		dict.Add(new KeyValuePair<string, int>("Δίος", 21));
        		Assert.AreEqual(2, dict.Count);
        		Assert.IsTrue(dict.ContainsKey("ΔΊΟΣ"));
        		Assert.IsTrue(dict.Contains(new KeyValuePair<string, int>("δίος", 21)));
        		Assert.IsFalse(dict.Contains(new KeyValuePair<string, int>("δίος", 3)));
        		Assert.IsTrue(dict.Keys.Contains("δίος"));
        		Assert.IsFalse(dict.Keys.Contains("δίοςδίος"));
        		Assert.IsTrue(dict.Values.Contains(770, new RoundedEquality()));
        		Assert.IsFalse(dict.Values.Contains(770));
        		int result;
        		Assert.IsTrue(dict.TryGetValue("ΔΊΟΣ", out result) && result == 21);
        		Assert.IsFalse(dict.TryGetValue("Eggplant", out result));
        		Assert.IsFalse(dict.Remove("aubergine"));
        		Assert.IsTrue(dict.Remove("Δίος"));
        		Assert.IsFalse(dict.Remove(new KeyValuePair<string, int>("WEISSBIER", 93)));
        		Assert.IsTrue(dict.Remove(new KeyValuePair<string, int>("WEISSBIER", 777)));
        		Assert.IsFalse(dict.ContainsKey("WEISSBIER"));
        		Assert.IsFalse(dict.Remove(new KeyValuePair<string, int>("WEISSBIER", 777)));
        		Assert.AreEqual(dict.Count, 0);
        		dict.Add("Palmer", 1111);
        		Assert.IsFalse(dict.Remove("Palmer", 1110, out result));
        		Assert.AreEqual(dict.Count, 1);
        		Assert.IsTrue(dict.Remove("Palmer", 1110, new RoundedEquality(), out result));
        		Assert.AreEqual(result, 1111);
        		Assert.AreEqual(dict.Count, 0);
        	}
        	[Test]
        	public void ConstantReturns()
        	{
        		var dict = new ThreadSafeDictionary<int, int>();
        		Assert.IsFalse(((ICollection<KeyValuePair<int, int>>)dict).IsReadOnly);
        		Assert.IsTrue(((ICollection<int>)dict.Keys).IsReadOnly && ((ICollection<int>)dict.Values).IsReadOnly);
        	}
        	[Test]
        	public void ResetEnumeator()
        	{
        		((IEnumerator)new ThreadSafeDictionary<int, int>().GetEnumerator()).Reset();
        	}
        	[Test]
        	public void ResetKeyEnumeator()
        	{
        		((IEnumerator)new ThreadSafeDictionary<int, int>().Keys.GetEnumerator()).Reset();
        	}
        	[Test]
        	public void ResetValueEnumeator()
        	{
        		((IEnumerator)new ThreadSafeDictionary<int, int>().Values.GetEnumerator()).Reset();
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void BadAdd()
        	{
        		var dict = new ThreadSafeDictionary<int, int>();
        		dict.Add(1, 1);
        		dict.Add(1, 2);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void BadAddKV()
        	{
        		var dict = new ThreadSafeDictionary<int, int>();
        		dict.Add(1, 1);
        		dict.Add(new KeyValuePair<int, int>(1, 2));
        	}
        	private bool EqualDicts<TKey, TValue>(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
        	{
        	    if(x.Count != y.Count)
        			return false;
        		TValue val;
        		foreach(KeyValuePair<TKey, TValue> kvp in x)
        			if(!y.TryGetValue(kvp.Key, out val) || !Equals(kvp.Value, val))
        				return false;
        		return true;
        	}
        	[Test]
        	public void Snapshots()
        	{
        		var dict = FilledStringDict();
        		var snap = dict.Clone();
        		var sd = dict.ToDictionary();
        		Assert.IsTrue(EqualDicts(dict, snap));
        		Assert.IsTrue(EqualDicts(snap, sd));
        	}
        	[Test]
        	[ExpectedException(typeof(KeyNotFoundException))]
        	public void KeyNotFound()
        	{
        		string val = FilledStringDict()["Well, it would be pretty amazing if this was found, wouldn’t it‽"];
        	}
        	private void NullCopy<T>(ICollection<T> seq)
        	{
        		seq.CopyTo(null, 0);
        	}
        	private void NegCopy<T>(ICollection<T> seq)
        	{
        		seq.CopyTo(new T[3], -2);
        	}
        	private void SmallCopy<T>(ICollection<T> seq)
        	{
        		seq.CopyTo(new T[seq.Count - 1], 1);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void KVNullCopy()
        	{
        		NullCopy(FilledStringDict());
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void KNullCopy()
        	{
        		NullCopy(FilledStringDict().Keys);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void VNullCopy()
        	{
        		NullCopy(FilledStringDict().Values);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentOutOfRangeException))]
        	public void KVNegCopy()
        	{
        		NegCopy(FilledStringDict());
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentOutOfRangeException))]
        	public void KNegCopy()
        	{
        		NegCopy(FilledStringDict().Keys);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentOutOfRangeException))]
        	public void VNegCopy()
        	{
        		NegCopy(FilledStringDict().Values);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void KVSmallCopy()
        	{
        		SmallCopy(FilledStringDict());
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void KSmallCopy()
        	{
        		SmallCopy(FilledStringDict().Keys);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void VSmallCopy()
        	{
        		SmallCopy(FilledStringDict().Values);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void NullComparer()
        	{
        		new ThreadSafeDictionary<int, int>((IEqualityComparer<int>)null);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void NullSourceCollection()
        	{
        		new ThreadSafeDictionary<int, int>((IEnumerable<KeyValuePair<int, int>>)null);
        	}
        	[Test]
        	public void CopyKVWithNullAndOffset()
        	{
        		var dict = FilledStringDict();
        		dict.Add(null, "null test");
        		KeyValuePair<string, string>[] arr = new KeyValuePair<string, string>[dict.Count + 5];
        		dict.CopyTo(arr, 5);
        		Assert.IsNotNull(arr[arr.Length - 1]);
        	}
        	[Test]
        	public void CopyKWithNullAndOffset()
        	{
        		var dict = FilledStringDict();
        		dict.Add(null, "null test");
        		string[] arr = new string[dict.Count + 5];
        		dict.Keys.CopyTo(arr, 5);
        		Assert.IsNotNull(arr[arr.Length - 1]);
        	}
        	[Test]
        	public void CopyVWithNullAndOffset()
        	{
        		var dict = FilledStringDict();
        		dict.Add(null, "null test");
        		string[] arr = new string[dict.Count + 5];
        		dict.Values.CopyTo(arr, 5);
        		Assert.IsNotNull(arr[arr.Length - 1]);
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantAddKeys()
        	{
        		((ICollection<string>)FilledStringDict().Keys).Add("X");
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantClearKeys()
        	{
        		((ICollection<string>)FilledStringDict().Keys).Clear();
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantRemoveKeys()
        	{
        		((ICollection<string>)FilledStringDict().Keys).Remove("X");
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantAddValues()
        	{
        		((ICollection<string>)FilledStringDict().Values).Add("X");
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantClearValues()
        	{
        		((ICollection<string>)FilledStringDict().Values).Clear();
        	}
        	[Test]
        	[ExpectedException(typeof(NotSupportedException))]
        	public void CantRemoveValues()
        	{
        		((ICollection<string>)FilledStringDict().Values).Remove("X");
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void TooSmallNullCopy()
        	{
        		KeyValuePair<string, string>[] arr = new KeyValuePair<string, string>[3];
        		var dict = FilledStringDict();
        		dict.Add(null, "Null Test");
        		dict.CopyTo(arr, 0);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void TooSmallNullKCopy()
        	{
        		string[] arr = new string[3];
        		var dict = FilledStringDict();
        		dict.Add(null, "Null Test");
        		dict.Keys.CopyTo(arr, 0);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentException))]
        	public void TooSmallNullVCopy()
        	{
        		string[] arr = new string[3];
        		var dict = FilledStringDict();
        		dict.Add(null, "Null Test");
        		dict.Values.CopyTo(arr, 0);
        	}
        	private class PathalogicalEqualityComparer : IEqualityComparer<int>
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
        		var dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 1000).Zip(Enumerable.Range(0, 1000), (k, v) => new KeyValuePair<int, int>(k, v * 2)));
        		for(int i = 999; i != -1; --i)
        			Assert.AreEqual(dict[i], i * 2);
        	}
        	[Test]
        	public void RemoveWhere()
        	{
        		var dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 1000).Select(x => new KeyValuePair<int, int>(x, x * 2)));
        		foreach(var kvp in dict.RemoveWhere((k, v) => k % 2 == 0))
        			Assert.AreEqual(kvp.Key % 2, 0);
        		foreach(var kvp in dict)
        			Assert.AreEqual(kvp.Key % 2, 1);
        		dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 1000).Select(x => new KeyValuePair<int, int>(x, x * 2)));
        		Assert.AreEqual(dict.Remove((k, v) => v % 4 == 0), 500);
        		Assert.AreEqual(dict.Count, 500);
        		foreach(var kvp in Enumerable.Range(1000, 4000).Select(x => new KeyValuePair<int, int>(x, x * 2)))
        			dict.Add(kvp);
        		Assert.AreEqual(dict.Count, 4500);
        	}
        	[Test]
        	public void Serialisation()
        	{
        	    //Note that the behaviour of the BinaryFormatter will fix some of the strings
        	    //used in many of these tests, as not being valid Unicode. This is desirable behaviour
        	    //in real code, but would give false negatives to this test.
        	    var dict = new ThreadSafeDictionary<string, string>();
        	    for(int i = 0; i != 10000; ++i)
        	        dict.Add(i.ToString(), (i * 2).ToString());
        	    dict.Add(null, "check null keys work");
        	    dict.Add("check null values work", null);
        	    using(MemoryStream ms = new MemoryStream())
        	    {
        	        new BinaryFormatter().Serialize(ms, dict);
    	            ms.Flush();
    	            ms.Seek(0, SeekOrigin.Begin);
    	            Assert.IsTrue(EqualDicts(dict, (ThreadSafeDictionary<string, string>)new BinaryFormatter().Deserialize(ms)));
        	    }
        	}
        	[Test]
        	public void TryAdd()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>();
        	    dict[0] = 0;
        	    Assert.IsTrue(dict.TryAdd(1, 2));
        	    Assert.IsFalse(dict.TryAdd(1, 9));
        	    Assert.IsTrue(dict.TryAdd(new KeyValuePair<int, int>(2, 4)));
        	    Assert.IsFalse(dict.TryAdd(new KeyValuePair<int, int>(2, 9)));
        	    Assert.IsFalse(dict.TryAdd(2, 20));
        	    Assert.IsTrue(dict.TryAdd(3, x => x * 2));
        	    Assert.IsFalse(dict.TryAdd(3, x => x * 4));
        	    int monitor = 99;
        	    Assert.IsTrue(dict.TryAdd(4, x => {
        	                                  return monitor = 8;
        	                              }));
        	    Assert.AreEqual(8, monitor);
        	    Assert.IsFalse(dict.TryAdd(4, x => {
        	                                   return monitor = 9;
        	                               }));
        	    Assert.AreEqual(8, monitor);
        	    Assert.IsTrue(dict.TryAdd(5, 10, out monitor));
        	    Assert.AreEqual(0, monitor);
        	    Assert.IsFalse(dict.TryAdd(5, 9, out monitor));
        	    Assert.AreEqual(10, monitor);
        	    Assert.IsTrue(dict.TryAdd(6, x => 12, out monitor));
        	    Assert.AreEqual(0, monitor);
        	    Assert.IsFalse(dict.TryAdd(6, x => 9, out monitor));
        	    Assert.AreEqual(12, monitor);
        	    foreach(var kvp in dict)
        	        Assert.AreEqual(kvp.Value, kvp.Key * 2);
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void TryAddNullFactory()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>();
        	    dict.TryAdd(1, null);
        	}
        	[Test]
        	public void Update()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 100).Select(x => new KeyValuePair<int, int>(x, 2 * x)));
        	    int val;
        	    Assert.IsFalse(dict.Update(10, 10, 1, EqualityComparer<int>.Default, out val));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.AreEqual(val, 20);
        	    Assert.IsTrue(dict.Update(10, 10, 20, EqualityComparer<int>.Default, out val));
        	    Assert.AreEqual(val, 20);
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, 10, EqualityComparer<int>.Default, out val));
        	    Assert.AreEqual(0, val);
        	    dict[10] = 20;
        	    Assert.IsFalse(dict.Update(10, 10, 1, EqualityComparer<int>.Default));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.IsTrue(dict.Update(10, 10, 20, EqualityComparer<int>.Default));
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, 10, EqualityComparer<int>.Default));
        	    dict[10] = 20;
        	    Assert.IsFalse(dict.Update(10, 10, 1,  out val));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.AreEqual(val, 20);
        	    Assert.IsTrue(dict.Update(10, 10, 20, out val));
        	    Assert.AreEqual(val, 20);
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, 10, out val));
        	    Assert.AreEqual(0, val);
        	    dict[10] = 20;
        	    Assert.IsFalse(dict.Update(10, 10, 1));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.IsTrue(dict.Update(10, 10, 20));
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, 10));
        	    dict[10] = 20;
        	    Assert.IsFalse(dict.Update(10, 10, x => x == 1, out val));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.AreEqual(val, 20);
        	    Assert.IsTrue(dict.Update(10, 10, x => x == 20, out val));
        	    Assert.AreEqual(val, 20);
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, x => true, out val));
        	    Assert.AreEqual(0, val);
        	    dict[10] = 20;
        	    Assert.IsFalse(dict.Update(10, 10, x => x == 1));
        	    Assert.AreNotEqual(10, dict[10]);
        	    Assert.IsTrue(dict.Update(10, 10, x => x == 20));
        	    Assert.AreEqual(10, dict[10]);
        	    Assert.IsFalse(dict.Update(-1, 10, x => true));
        	    dict[10] = 20;
        	}
        	[Test]
        	[ExpectedException(typeof(ArgumentNullException))]
        	public void UpdateNullFactory()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>();
        	    dict.Update(1, 1, null);
        	}
        	[Test]
        	public void GetOrAdd()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 100).Select(x => new KeyValuePair<int, int>(x, 2 * x)));
        	    int val;
        	    Assert.IsTrue(dict.GetOrAdd(200, 400, out val));
        	    Assert.AreEqual(400, val);
        	    Assert.AreEqual(400, dict[200]);
        	    Assert.IsFalse(dict.GetOrAdd(200, 300, out val));
        	    Assert.AreEqual(400, val);
        	    Assert.AreEqual(400, dict[200]);
        	    Assert.AreEqual(400, dict.GetOrAdd(200, 10));
        	    Assert.AreEqual(400, dict[200]);
        	    Assert.AreEqual(600, dict.GetOrAdd(300, 600));
        	    Assert.AreEqual(600, dict[300]);
        	    bool called = false;
        	    Assert.IsFalse(dict.GetOrAdd(200, x => {
        	                                     called = true;
        	                                     return 50;
        	                                 }, out val));
        	    Assert.AreEqual(400, val);
        	    Assert.AreEqual(400, dict[200]);
        	    Assert.IsFalse(called);
        	    Assert.IsTrue(dict.GetOrAdd(400, x => {
        	                                     called = true;
        	                                     return 800;
        	                                 }, out val));
        	    Assert.AreEqual(800, val);
        	    Assert.AreEqual(800, dict[400]);
        	    Assert.IsTrue(called);
        	    called = false;
        	    Assert.AreEqual(400, dict.GetOrAdd(200, x => {
        	                                           called = true;
        	                                           return 50;
        	                                       }));
        	    Assert.AreEqual(400, dict[200]);
        	    Assert.IsFalse(called);
        	    Assert.AreEqual(-2, dict.GetOrAdd(-1, x => {
        	                                          called = true;
        	                                          return -2;
        	                                      }));
        	    Assert.AreEqual(-2, dict[-1]);
        	    Assert.IsTrue(called);
        	}
        	[Test]
        	public void AddOrUpdate()
        	{
        	    var dict = new ThreadSafeDictionary<int, int>(Enumerable.Range(0, 10).Select(x => new KeyValuePair<int, int>(x, 2 * x)));
        	    int val;
        	    Assert.IsTrue(dict.AddOrUpdate(10, 18, 99, EqualityComparer<int>.Default, out val));
        	    Assert.AreEqual(0, val);
        	    Assert.AreEqual(18, dict[10]);
        	    Assert.IsFalse(dict.AddOrUpdate(10, 20, 99, EqualityComparer<int>.Default, out val));
        	    Assert.AreEqual(18, val);
        	    Assert.AreEqual(18, dict[10]);
        	    Assert.IsTrue(dict.AddOrUpdate(10, 20, 18, EqualityComparer<int>.Default, out val));
        	    Assert.AreEqual(18, val);
        	    Assert.AreEqual(20, dict[10]);
    
        	    Assert.IsTrue(dict.AddOrUpdate(100, 180, 99, EqualityComparer<int>.Default));
        	    Assert.AreEqual(180, dict[100]);
        	    Assert.IsFalse(dict.AddOrUpdate(100, 200, 99, EqualityComparer<int>.Default));
        	    Assert.AreEqual(180, dict[100]);
        	    Assert.IsTrue(dict.AddOrUpdate(100, 200, 180, EqualityComparer<int>.Default));
        	    Assert.AreEqual(200, dict[100]);
        	    
        	    dict.Remove(10);
        	    dict.Remove(100);
        	    
        	    Assert.IsTrue(dict.AddOrUpdate(10, 18, 99, out val));
        	    Assert.AreEqual(0, val);
        	    Assert.AreEqual(18, dict[10]);
        	    Assert.IsFalse(dict.AddOrUpdate(10, 20, 99, out val));
        	    Assert.AreEqual(18, val);
        	    Assert.AreEqual(18, dict[10]);
        	    Assert.IsTrue(dict.AddOrUpdate(10, 20, 18, out val));
        	    Assert.AreEqual(18, val);
        	    Assert.AreEqual(20, dict[10]);
    
        	    Assert.IsTrue(dict.AddOrUpdate(100, 180, 99));
        	    Assert.AreEqual(180, dict[100]);
        	    Assert.IsFalse(dict.AddOrUpdate(100, 200, 99));
        	    Assert.AreEqual(180, dict[100]);
        	    Assert.IsTrue(dict.AddOrUpdate(100, 200, 180));
        	    Assert.AreEqual(200, dict[100]);
        	    
        	    bool calledUpdater = false;
        	    
        	    Assert.AreEqual(18, dict.AddOrUpdate(20, 18, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }, out val));
        	    Assert.IsFalse(calledUpdater);
        	    Assert.AreEqual(0, val);
        	    Assert.AreEqual(18, dict[20]);
        	    Assert.AreEqual(20, dict.AddOrUpdate(20, 18, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }, out val));
        	    Assert.IsTrue(calledUpdater);
        	    Assert.AreEqual(18, val);
        	    Assert.AreEqual(20, dict[20]);
        	    
        	    calledUpdater = false;
        	    dict.Remove(20);
        	    Assert.AreEqual(18, dict.AddOrUpdate(20, 18, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }));
        	    Assert.IsFalse(calledUpdater);
        	    Assert.AreEqual(18, dict[20]);
        	    Assert.AreEqual(20, dict.AddOrUpdate(20, 18, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }));
        	    Assert.IsTrue(calledUpdater);
        	    Assert.AreEqual(20, dict[20]);
        	    
        	    calledUpdater = false;
        	    bool calledFactory = false;
        	    dict.Remove(20);
        	    
        	    Assert.AreEqual(18, dict.AddOrUpdate(20, k => {
        	                                             calledFactory = true;
        	                                             return 18;
        	                                         }, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }, out val));
        	    Assert.IsTrue(calledFactory);
        	    Assert.IsFalse(calledUpdater);
        	    Assert.AreEqual(18, dict[20]);
        	    Assert.AreEqual(0, val);
        	    calledFactory = false;
        	    Assert.AreEqual(20, dict.AddOrUpdate(20, k => {
        	                                             calledFactory = true;
        	                                             return 18;
        	                                         }, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }, out val));
        	    Assert.IsFalse(calledFactory);
        	    Assert.IsTrue(calledUpdater);
        	    Assert.AreEqual(20, dict[20]);
        	    Assert.AreEqual(18, val);
    
    
        	    dict.Remove(20);
        	    calledUpdater = false;
        	    
        	    Assert.AreEqual(18, dict.AddOrUpdate(20, k => {
        	                                             calledFactory = true;
        	                                             return 18;
        	                                         }, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }));
        	    Assert.IsTrue(calledFactory);
        	    Assert.IsFalse(calledUpdater);
        	    Assert.AreEqual(18, dict[20]);
        	    calledFactory = false;
        	    Assert.AreEqual(20, dict.AddOrUpdate(20, k => {
        	                                             calledFactory = true;
        	                                             return 18;
        	                                         }, (k, v) => {
        	                                             calledUpdater = true;
        	                                             return 20;
        	                                         }));
        	    Assert.IsFalse(calledFactory);
        	    Assert.IsTrue(calledUpdater);
        	    Assert.AreEqual(20, dict[20]);
        	}
        	private class FinaliserCounted : IEquatable<FinaliserCounted>
        	{
        	    public static int FinalCount;
        	    public readonly int Value;
        	    public FinaliserCounted(int value)
        	    {
        	        Value = value;
        	    }
                public bool Equals(FinaliserCounted other)
                {
                    return other != null && other.Value == Value;
                }
                public override bool Equals(object obj)
                {
                    return Equals(obj as FinaliserCounted);
                }
                public override int GetHashCode()
                {
                    return Value;
                }
                ~FinaliserCounted()
                {
                    Interlocked.Increment(ref FinalCount);
                }
        	}
        }
        namespace MultiThreaded
        {
            public abstract class MultiThreadTests
        	{
            	protected const int SourceDataLen = 0x40000;
            	protected static string[] SourceData = new string[SourceDataLen];
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
                }
                protected Dictionary<string, string> FilledStringCompareDict()
                {
            		var dict = new Dictionary<string, string>();
            		for(int i = 0; i != SourceDataLen; i+= 2)
            			dict[SourceData[i]] = SourceData[i + 1];
            		return dict;
                }
            	protected bool EqualDicts<TKey, TValue>(IDictionary<TKey, TValue> x, IDictionary<TKey, TValue> y)
            	{
            		if(x.Count != y.Count)
            			return false;
            		TValue val;
            		foreach(KeyValuePair<TKey, TValue> kvp in x)
            		{
            			if(!y.TryGetValue(kvp.Key, out val) || !Equals(kvp.Value, val))
            				return false;
            		}
            		return true;
            	}
        		protected IntPtr _affinity;
                private readonly IntPtr _startingAffinity;
                private readonly bool SkipForAffinity;
        		protected Thread[] _threads;
        		protected object[] _params;
        		protected int CoreCount;
        		public MultiThreadTests()
        		{
        			_startingAffinity = _affinity = Process.GetCurrentProcess().ProcessorAffinity;
        			for(int aff = (int)_affinity; aff != 0; aff >>= 1)
        				if((aff & 1) != 0)
        					CoreCount++;
                    if(CoreCount == 0)
                    {
                        SkipForAffinity = NeedsAffinityAccess;
                        CoreCount = Environment.ProcessorCount;
                    }
        			FillSourceData();
        		}
                protected abstract bool NeedsAffinityAccess { get; }
                private void SkipIfCantChangeAffinity()
                {
                    if(SkipForAffinity)
                        Assert.Ignore("This test needs to change the processor affinity, which the current framework version does not allow");
                }
        		protected abstract void SetupThreads();
        		[SetUp]
        		public void SetUp()
        		{
        			SetupThreads();
        			_params = new object[_threads.Length];
        		}
        		[TearDown]
        		public void TearDown()
        		{
        			Process.GetCurrentProcess().ProcessorAffinity = _startingAffinity;
        		}
        		private void StartThreads(int end = 0)
        		{
        		    if(end == 0)
        		        end = _threads.Length;
        			for(int i = 0; i != end; ++i)
        				if(_params[i] == null)
        					_threads[i].Start();
        				else
        					_threads[i].Start(_params[i]);
        		}
        		private void EndThreads()
        		{
        			foreach(Thread thread in _threads)
        				if(thread != null)
        					thread.Join();
        		}
        		[Test]
        		public void MultiWriteSame()
        		{
                    SkipIfCantChangeAffinity();
        			var dict = new ThreadSafeDictionary<string, string>();
        			for(int i = 0; i != _threads.Length; ++i)
        			{
        				_threads[i] = new Thread(WriteSome);
        				_params[i] = Tuple.Create(dict, 0, SourceDataLen);
        			}
        			StartThreads();
        			EndThreads();
        			for(int i = 0; i != SourceDataLen; i += 2)
        				Assert.AreEqual(dict[SourceData[i]], SourceData[i + 1]);
        		}
        		private void WriteSome(object param)
        		{
        			var tup = (Tuple<ThreadSafeDictionary<string, string>, int, int>)param;
        			var dict = tup.Item1;
        			var end = tup.Item2 + tup.Item3;
        			for(int i = tup.Item2; i != end; i += 2)
        			{
        				dict[SourceData[i]] = SourceData[i + 1];
        			}
        		}
        		[Test]
        		public void MultiWriteParts()
        		{
                    SkipIfCantChangeAffinity();
        			var dict = new ThreadSafeDictionary<string, string>();
        			for(int i = 0; i != _threads.Length; ++i)
        			{
        				_threads[i] = new Thread(WriteSome);
        				_params[i] = Tuple.Create(dict, (SourceDataLen / _threads.Length * i) % SourceDataLen, SourceDataLen / _threads.Length);
        			}
        			StartThreads();
        			EndThreads();
        			for(int i = 0; i != SourceDataLen; i += 2)
        				Assert.AreEqual(dict[SourceData[i]], SourceData[i + 1]);
        		}
        		[Test]
        		public void RemoveSomeAsWeGo()
        		{
                    SkipIfCantChangeAffinity();
        			unchecked
        			{
        				var dict = new ThreadSafeDictionary<int, int>();
        				int len = _threads.Length - 1;
        				if(len < 1)
        				    len = 1;
        				int gap = Math.Min(10, len);
        				int shouldStop = 0;
        				for(int i = 0; i != len; ++i)
        				{
        					_threads[i] = new Thread((object obj) => {
        					                         	int x = (int)obj;
        					                         	while(x < 200000)
        					                         	{
        					                         		dict[x] = x * x;
        					                         		x += gap;
        					                         	}
        					                         });
        				    _params[i] = i % gap;
        					
        				}
        				StartThreads(len);
        				Thread remover = new Thread(() => {
        				                                while(shouldStop == 0)
        				                            	   dict.Remove((k, v) => k % 100 == 0);
        				                                dict.Remove((k, v) => k % 100 == 0);//one more sweep.
        				                            });
        				remover.Start();
        				EndThreads();
        				Interlocked.Exchange(ref shouldStop, 1);
        				remover.Join();
        				for(int i = 0; i != 200000; ++i)
        					if(i % 100 == 0)
        						Assert.IsFalse(dict.ContainsKey(i));
        					else
        						Assert.AreEqual(dict[i], i * i);
        			}
        		}
        		[Test]
        		public void RacingWrite()
        		{
                    SkipIfCantChangeAffinity();
        		    var dict = new ThreadSafeDictionary<int, int>();
    		        for(int i = 0; i != _threads.Length; ++i)
    		        {
    		            _threads[i] = new Thread((object obj) =>
    		                                     {
    		                                         int offset = (int)obj;
    		                                         for(int j = 0; j != 21000; ++j)
    		                                             dict[j] = j % 3;
    		                                         for(int j = 0; j != 20000; ++j)
    		                                             dict[j] = offset + j % 10;
    		                                     });
    		            _params[i] = i * 10;
    		        }
    		        StartThreads();
    		        EndThreads();
    		        for(int i = 0; i != 20000; ++i)
    		            Assert.AreEqual(i % 10, dict[i] % 10);
    		        for(int i = 20000; i != 21000; ++i)
    		            Assert.AreEqual(i % 3, dict[i] % 3);
        		}
        	}
        	[TestFixture]
        	public class AllCores : MultiThreadTests
        	{
        		protected override void SetupThreads()
        		{
        			_threads = new Thread[CoreCount * 16];
        		}
                protected override bool NeedsAffinityAccess
                {
                    get { return false; }
                }
        	}
        	[TestFixture]
        	public class OnePerCore : MultiThreadTests
        	{
        		protected override void SetupThreads()
        		{
        			_threads = new Thread[CoreCount];
        		}
                protected override bool NeedsAffinityAccess
                {
                    get { return false; }
                }
        	}
        	[TestFixture]
        	public class OneCore : MultiThreadTests
        	{
        		protected override void SetupThreads()
        		{
        			uint i = 1;
        			uint curAff = (uint)_affinity;
        			while(i < 0x80000000)
        			{
        				uint newAff = curAff & i;
        				if(newAff != 0)
        				{
        					Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)newAff;
        					break;
        				}
        				i <<= 1;
        			}
        			_threads = new Thread[16];
        		}
                protected override bool NeedsAffinityAccess
                {
                    get { return true; }
                }
        	}
        	//With current implementations, hyperthreaded cores will be given every
        	//other core from the set of virtual processors, hence masking with
        	//0x55555555 or 0xAAAAAAAA will ensure that hypethreading is not used
        	//and test for this leading to different behaviour.
        	[TestFixture]
        	public class NoHyperThreading : MultiThreadTests
        	{
        		protected override void SetupThreads()
        		{
        			uint curAff = (uint)_affinity;
        			uint newAff = curAff & 0xAAAAAAAA;
        			if(newAff != 0)
        				Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)newAff;
        			else
        			{
        				newAff = curAff & 0x55555555;
        				if(newAff != 0)
        					Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)newAff;
        			}
        			_threads = new Thread[8 * CoreCount];
        		}
                protected override bool NeedsAffinityAccess
                {
                    get { return true; }
                }
        	}
        	[TestFixture]
        	public class NoHyperThreadingOnePerCore : MultiThreadTests
        	{
        		protected override void SetupThreads()
        		{
        			uint curAff = (uint)_affinity;
        			uint newAff = curAff & 0xAAAAAAAA;
        			if(newAff != 0)
        				Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)newAff;
        			else
        			{
        				newAff = curAff & 0x55555555;
        				if(newAff != 0)
        					Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)newAff;
        			}
        			_threads = new Thread[CoreCount / 2];
        		}
                protected override bool NeedsAffinityAccess
                {
                    get { return false; }
                }
        	}
        }
    }
}