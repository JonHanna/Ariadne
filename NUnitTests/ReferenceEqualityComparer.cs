using System;
using NUnit.Framework;

namespace Ariadne.NUnitTests
{
    [TestFixture]
    public class ReferenceEqualityComparer
    {
        private class RepBaseHC : IEquatable<RepBaseHC>
        {
            private int _val;
            public RepBaseHC(int val)
            {
                _val = val;
            }
            public bool Equals(RepBaseHC other)
            {
                return other != null && _val == other._val;
            }
            public override bool Equals(object obj)
            {
                return Equals(obj as RepBaseHC);
            }
            public override int GetHashCode()
            {
                return _val;
            }
            public int GetBaseHashCode()
            {
                return base.GetHashCode();
            }
        }
        [Test]
        public void ReferenceEquality()
        {
            RepBaseHC x = new RepBaseHC(1);
            RepBaseHC y = new RepBaseHC(1);
            Assert.AreEqual(x, y);
            Assert.AreEqual(x.GetHashCode(), y.GetHashCode());
            ReferenceEqualityComparer<RepBaseHC> eq = new ReferenceEqualityComparer<RepBaseHC>();
            Assert.AreEqual(eq.GetHashCode(x), x.GetBaseHashCode());
            Assert.AreEqual(eq.GetHashCode(y), y.GetBaseHashCode());
            Assert.IsFalse(eq.Equals(x, y));
            Assert.AreEqual(0, eq.GetHashCode(null));
        }
    }
}
