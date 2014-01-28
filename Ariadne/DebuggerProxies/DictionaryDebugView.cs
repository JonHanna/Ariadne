// DictionaryDebugView.cs
//
// Author:
//     Jon Hanna <jon@hackcraft.net>
//
// © 2014 Jon Hanna
//
// Licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System.Collections.Generic;
using System.Diagnostics;

namespace Ariadne.DebuggerProxies
{
    internal class DictionaryDebugView<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> _dict;
        public DictionaryDebugView(IDictionary<TKey, TValue> dictionary)
        {
            _dict = dictionary;
        }
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public KeyValuePair<TKey, TValue>[] Items
        {
            get { return new List<KeyValuePair<TKey, TValue>>(_dict).ToArray(); }
        }
    }
}

