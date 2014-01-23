// Strings.cs
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
using System.Globalization;
using System.Resources;

namespace Ariadne
{
    internal static class Strings
    {
        private static readonly ResourceManager RM = new ResourceManager("Ariadne", typeof(Strings).Assembly);
        public static string DictNullSourceCollection
        {
            get { return RM.GetString("Dict_Null_Source_Collection"); }
        }
        public static string SetNullSourceCollection
        {
            get { return RM.GetString("Set_Null_Source_Collection"); }
        }
        public static string DictSameKey
        {
            get { return RM.GetString("Dict_Same_Key"); }
        }
        public static string CopyToArrayTooSmall
        {
            get { return RM.GetString("Copy_To_Array_Too_Small"); }
        }
        public static string SyncRootNotSupported
        {
            get { return RM.GetString("SyncRoot_Not_Supported"); }
        }
        public static string CantCastNullToValueType(Type type)
        {
            return string.Format(CultureInfo.CurrentCulture, RM.GetString("Cant_Cast_Null_To_Value_Type"), type.FullName);
        }
        public static string InvalidCastKeys(Type argument, Type target)
        {
            return string.Format(CultureInfo.CurrentCulture, RM.GetString("Invalid_Cast_Keys"), argument.FullName, target.FullName);
        }
        public static string InvalidCastValues(Type argument, Type target)
        {
            return string.Format(CultureInfo.CurrentCulture, RM.GetString("Invalid_Cast_Values"), argument.FullName, target.FullName);
        }
        public static string CantCopyMultidimensional
        {
            get { return RM.GetString("Cant_Copy_Multidimensional"); }
        }
        public static string CantCopyNonZero
        {
            get { return RM.GetString("Cant_Copy_NonZero"); }
        }
        public static string ReadOnlyCollection
        {
            get { return RM.GetString("Readonly_Collection"); }
        }
        public static string ResettingNotSupportedBySource
        {
            get { return RM.GetString("Resetting_Not_Supported_By_Source"); }
        }
        public static string RetrievingNonReference
        {
            get { return RM.GetString("Retrieving_Non_Reference"); }
        }
    }
}