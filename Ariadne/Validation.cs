// Validation.cs
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

using System;

namespace Ariadne
{
    internal static class Validation
    {
        public static void CopyTo<T>(T[] array, int arrayIndex)
        {
            NullCheck(array, "array");
            Positive(arrayIndex, "arrayIndex");
        }
        public static void CopyTo(Array array, int index)
        {
            NullCheck(array, "array");
            Positive(index, "index");
            if(array.Rank == 1)
            {
                if(array.GetLowerBound(0) == 0)
                    return;
                throw new ArgumentException(Strings.CantCopyNonZero, "array");
            }
            throw new ArgumentException(Strings.CantCopyMultidimensional, "array");
        }
        public static void NullCheck<T>(T value, string paramName)
        {
            // Analysis disable once CompareNonConstrainedGenericWithNull
            // Might want to check for case of T is reference type AND T is null
            if(value != null)
                return;
            throw new ArgumentNullException(paramName);
        }
        public static void PositiveNonZero(int value, string paramName)
        {
            if(value > 0)
                return;
            throw new ArgumentOutOfRangeException(paramName);
        }
        public static void Positive(int value, string paramName)
        {
            if(value >= 0)
                return;
            throw new ArgumentOutOfRangeException(paramName);
        }
        public static void NotSupportedReadOnly()
        {
            throw new NotSupportedException(Strings.ReadOnlyCollection);
        }
    }
}