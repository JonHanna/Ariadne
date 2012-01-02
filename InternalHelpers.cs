// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Resources;
using System.Threading;

namespace HackCraft.LockFree
{
    //Simple means to share an atomically-maintained count between objects.
    internal sealed class AliasedInt
    {
        private int _value;
        public static implicit operator int(AliasedInt ri)
        {
            return ri._value;
        }
        public int Increment()
        {
            return Interlocked.Increment(ref _value);
        }
        public int Decrement()
        {
            return Interlocked.Decrement(ref _value);
        }
    }
    internal sealed class SinglyLinkedNode<T>
    {
        public T Item;
        public SinglyLinkedNode<T> Next;
        public SinglyLinkedNode(T item)
        {
            Item = item;
        }
    }
    internal static class Strings
    {
        private static readonly ResourceManager rm = new ResourceManager("HackCraft.LockFree", typeof(Strings).Assembly);
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
            return string.Format(rm.GetString("Cant_Cast_Null_To_Value_Type"), type.FullName);
        }
        public static string Invalid_Cast_Keys(Type argument, Type target)
        {
            return string.Format(rm.GetString("Invalid_Cast_Keys"), argument.FullName, target.FullName);
        }
        public static string Invalid_Cast_Values(Type argument, Type target)
        {
            return string.Format(rm.GetString("Invalid_Cast_Values"), argument.FullName, target.FullName);
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
