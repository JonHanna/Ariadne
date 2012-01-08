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

// (I was using this with some experimenting, and it seemed of reasonable enough general use).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace Ariadne.Collections
{
    /// <summary>An implementation of <see cref="IEqualityComparer"/> which
    /// compares for object identity (reference equality), ignoring any overrides
    /// of <see cref="object.Equals(object)"/> and <see cref="object.GetHashCode()"/>
    /// or any implementation of <see cref="IEquatable&lt;T>"/>.</summary>
    /// <threadsafety static="true" instance="true"/>
    public class ReferenceEqualityComparer : IEqualityComparer
    {
        protected static Func<object, int> RootHashCode;
        static ReferenceEqualityComparer()
        {
            // Note: If moved to .NET 2.0 prior to SP 1, requires ReflectionPermission with ReflectionPermissionFlag.ReflectionEmit
            DynamicMethod dynM = new DynamicMethod(string.Empty, typeof(int), new Type[]{typeof(object)}, typeof(object));
            ILGenerator ilGen = dynM.GetILGenerator(7);
            ilGen.Emit(OpCodes.Ldarg_0);
            ilGen.Emit(OpCodes.Call, typeof(object).GetMethod("GetHashCode"));
            ilGen.Emit(OpCodes.Ret);
            
            RootHashCode = (Func<object, int>)dynM.CreateDelegate(typeof(Func<object, int>));
        }
        /// <summary>Returns true if the two arguments are the same object, false otherwise.</summary>
        /// <param name="x">The first item to compare.</param>
        /// <param name="y">The second item to compare.</param>
        /// <returns>True if they are the same object, false otherwise.</returns>
        public bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }
        /// <summary>Returns the identity-based hash-code defined in <see cref="object"/>,
        /// ignoring any overrides.</summary>
        /// <param name="obj">The object to obtain a hash-code for.</param>
        /// <returns>The hash-code.</returns>
        public int GetHashCode(object obj)
        {
            return RootHashCode(obj);
        }
    }
    /// <summary>An implementation of <see cref="IEqualityComparer&lt;T>"/> which
    /// compares for object identity (reference equality), ignoring any overrides
    /// of <see cref="object.Equals(object)"/> and <see cref="object.GetHashCode()"/>
    /// or any implementation of <see cref="IEquatable&lt;T>"/>.</summary>
    /// <typeparam name="T">The type of objects to compare.</typeparam>
    /// <threadsafety static="true" instance="true"/>
    public class ReferenceEqualityComparer<T> : ReferenceEqualityComparer, IEqualityComparer<T> where T : class
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
            return RootHashCode(obj);
        }
    }
}
