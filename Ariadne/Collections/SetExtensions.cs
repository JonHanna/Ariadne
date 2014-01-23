// SetExtensions.cs
//
// Author:
//     Jon Hanna <jon@hackcraft.net>
//
// © 2011–2014 Jon Hanna
//
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

namespace Ariadne.Collections
{
    /// <summary>Provides further static methods for manipulating <see cref="ThreadSafeSet&lt;T>"/>’s with
    /// particular parameter types. In C♯ and VB.NET these extension methods can be called as instance methods on
    /// appropriately typed <see cref="ThreadSafeSet&lt;T>"/>s.</summary>
    /// <threadsafety static="true" instance="true"/>
    public static class SetExtensions
    {
        /// <summary>Retrieves a reference to the specified item.</summary>
        /// <typeparam name="T">The type of the items in the set.</typeparam>
        /// <param name="tset">The set to search.</param>
        /// <param name="item">The item sought.</param>
        /// <returns>A reference to a matching item if it is present in the set, null otherwise.</returns>
        /// <remarks>This allows use of the set to restrain a group of objects to exclude duplicates, allowing for
        /// reduced memory use, and reference-based equality checking, comparable with how
        /// <see cref="string.IsInterned(string)"/> allows one to check for a copy of a string in the CLR intern pool,
        /// but also allowing for removal, clearing and multiple pools. This is clearly only valid for reference types.
        /// </remarks>
        public static T Find<T>(this ThreadSafeSet<T> tset, T item) where T : class
        {
            ThreadSafeSet<T>.Box box = tset.Obtain(item);
            return box == null ? null : box.Value;
        }

        /// <summary>Retrieves a reference to the specified item, adding it if necessary.</summary>
        /// <typeparam name="T">The type of the items in the set.</typeparam>
        /// <param name="tset">The set to search, and add to if necessary.</param>
        /// <param name="item">The item sought.</param>
        /// <returns>A reference to a matching item if it is present in the set, using the item given if there isn’t
        /// already a matching item.</returns>
        /// <exception cref="System.InvalidOperationException"> An attempt was made to use this when the generic type of
        /// the set is not a reference type (that is, a value or pointer type).</exception>
        /// <remarks>This allows use of the set to restrain a group of objects to exclude duplicates, allowing for
        /// reduced memory use, and reference-based equality checking, comparable with how
        /// <see cref="string.Intern(string)"/> allows one to check for a copy of a string in the CLR intern pool, but
        /// also allowing for removal, clearing and multiple pools. This is clearly only valid for reference types.
        /// </remarks>
        public static T FindOrStore<T>(this ThreadSafeSet<T> tset, T item) where T : class
        {
            ThreadSafeSet<T>.Box found = tset.PutIfMatch(new ThreadSafeSet<T>.Box(item));
            return found == null || found is ThreadSafeSet<T>.TombstoneBox ? item : found.Value;
        }
    }
}