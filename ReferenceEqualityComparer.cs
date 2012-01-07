using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Ariadne
{
    internal static class ObjectHashCodeCalculater
    {
        private static Func<object, int> rootHashCode;
        static ObjectHashCodeCalculater()
        {
            // Note: If moved to .NET 2.0 prior to SP 1, requires ReflectionPermission with ReflectionPermissionFlag.ReflectionEmit
            DynamicMethod dynM = new DynamicMethod(string.Empty, typeof(int), new Type[]{typeof(object)}, typeof(object));
            ILGenerator ilGen = dynM.GetILGenerator(8);
            ilGen.Emit(OpCodes.Ldarg_0);
            ilGen.Emit(OpCodes.Call, typeof(object).GetMethod("GetHashCode"));
            ilGen.Emit(OpCodes.Ret);
            
            rootHashCode = (Func<object, int>)dynM.CreateDelegate(typeof(Func<object, int>));
        }
        public static int GetRootHashCode(object obj)
        {
            return rootHashCode(obj);
        }
    }
    /// <summary>An implementation of <see cref="IEqualityComparer&lt;T>"/> which
    /// compares for object identity (reference equality), ignoring any overrides
    /// of <see cref="object.Equals(object)"/> and <see cref="object.GetHashCode()"/>
    /// or any implementation of <see cref="IEquatable&lt;T>"/>.</summary>
    /// <typeparam name="T">The type of objects to compare.</typeparam>
    /// <threadsafety static="true" instance="true"/>
    public class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        /// <summary>Returns true if the two arguments are the same object, false otherwise.</summary>
        /// <param name="x">The first item to compare.</param>
        /// <param name="y">The second item to compare.</param>
        /// <returns>True if they are the same object, false otherwise.</returns>
        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }
        /// <summary>Returns the identity-based hash-code defined in <see cref="object"/>,
        /// ignoring any overrides.</summary>
        /// <param name="obj">The object to obtain a hash-code for.</param>
        /// <returns>The hash-code.</returns>
        public int GetHashCode(T obj)
        {
            return ObjectHashCodeCalculater.GetRootHashCode(obj);
        }
    }
}
