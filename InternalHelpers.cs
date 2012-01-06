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
using System.Globalization;
using System.Resources;

namespace Ariadne
{

    internal sealed class SinglyLinkedNode<T>
    {
        public T Item;
        public SinglyLinkedNode<T> Next;
        public SinglyLinkedNode(T item)
        {
            Item = item;
        }
    }
    internal static class Validation
    {
        public static void CopyTo<T>(T[] array, int arrayIndex)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(arrayIndex < 0)
        		throw new ArgumentOutOfRangeException("arrayIndex");
        }
        public static void CopyTo(Array array, int index)
        {
        	if(array == null)
        		throw new ArgumentNullException("array");
        	if(array.Rank != 1)
        	    throw new ArgumentException(Strings.Cant_Copy_Multidimensional, "array");
        	if(array.GetLowerBound(0) != 0)
        	    throw new ArgumentException(Strings.Cant_Copy_NonZero, "array");
        	if(index < 0)
        		throw new ArgumentOutOfRangeException("index");
        }
    }
    internal static class Strings
    {
        private static readonly ResourceManager rm = new ResourceManager("Ariadne", typeof(Strings).Assembly);
        public static string Dict_Null_Source_Collection
        {
            get { return rm.GetString("Dict_Null_Source_Collection"); }
        }
        public static string Set_Null_Source_Collection
        {
            get { return rm.GetString("Set_Null_Source_Collection"); }
        }
        public static string Dict_Same_Key
        {
            get { return rm.GetString("Dict_Same_Key"); }
        }
        public static string Copy_To_Array_Too_Small
        {
            get { return rm.GetString("Copy_To_Array_Too_Small"); }
        }
        public static string SyncRoot_Not_Supported
        {
            get { return rm.GetString("SyncRoot_Not_Supported"); }
        }
        public static string Cant_Cast_Null_To_Value_Type(Type type)
        {
            return string.Format(CultureInfo.CurrentCulture, rm.GetString("Cant_Cast_Null_To_Value_Type"), type.FullName);
        }
        public static string Invalid_Cast_Keys(Type argument, Type target)
        {
            return string.Format(CultureInfo.CurrentCulture, rm.GetString("Invalid_Cast_Keys"), argument.FullName, target.FullName);
        }
        public static string Invalid_Cast_Values(Type argument, Type target)
        {
            return string.Format(CultureInfo.CurrentCulture, rm.GetString("Invalid_Cast_Values"), argument.FullName, target.FullName);
        }
        public static string Cant_Copy_Multidimensional
        {
            get { return rm.GetString("Cant_Copy_Multidimensional"); }
        }
        public static string Cant_Copy_NonZero
        {
            get { return rm.GetString("Cant_Copy_NonZero"); }
        }
        public static string Resetting_Not_Supported_By_Source
        {
            get { return rm.GetString("Resetting_Not_Supported_By_Source"); }
        }
        public static string Retrieving_Non_Reference
        {
            get { return rm.GetString("Retrieving_Non_Reference"); }
        }
    }
}
