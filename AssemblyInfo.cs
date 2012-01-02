// © 2011 Jon Hanna.
// This source code is licensed under the EUPL, Version 1.1 only (the “Licence”).
// You may not use, modify or distribute this work except in compliance with the Licence.
// You may obtain a copy of the Licence at:
// <http://joinup.ec.europa.eu/software/page/eupl/licence-eupl>
// A copy is also distributed with this source code.
// Unless required by applicable law or agreed to in writing, software distributed under the
// Licence is distributed on an “AS IS” basis, without warranties or conditions of any kind.

using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security;

[assembly: AssemblyTitle("HackCraft LockFree Collections")]
[assembly: AssemblyDescription("Lock-free thread safe collection classes.")]
[assembly: AssemblyProduct("HackCraft LockFree Collections Library")]
[assembly: AssemblyCopyright("© 2011 Jon Hanna. Released under the European Union Public Licence v1.1")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: CLSCompliant(true)]
[assembly: AssemblyVersion("0.1.*")]
#if DEBUG
[assembly: AssemblyConfiguration("Debug")]
#else
[assembly: AssemblyConfiguration("Release")]
[assembly: AllowPartiallyTrustedCallers]
#endif
[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.MainAssembly)]