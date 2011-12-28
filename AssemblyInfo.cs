using System;
using System.Reflection;
using System.Runtime.InteropServices;

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
#endif