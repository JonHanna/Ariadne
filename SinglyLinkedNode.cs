// © 2011 Jon Hanna.
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System.Diagnostics.CodeAnalysis;

namespace Ariadne
{
    [SuppressMessage("Microsoft.StyleCop.CSharp.MaintainabilityRules",
        "SA1401:FieldsMustBePrivate", Justification = "Need to be able to CAS it.")]
    internal sealed class SinglyLinkedNode<T>
    {
        public T Item;
        public SinglyLinkedNode<T> Next;
        public SinglyLinkedNode(T item)
        {
            Item = item;
        }
    }
}