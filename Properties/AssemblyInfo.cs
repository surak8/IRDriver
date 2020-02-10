using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly:AssemblyTitle("IRDriver")]
[assembly:AssemblyProduct("IRDriver")]
[assembly:AssemblyDescription("description of IRDriver.")]
[assembly:AssemblyCompany("Colt Manufacturing Company LLC")]
[assembly:AssemblyCopyright("Copyright © 2020, Colt Manufacturing Company LLC")]
#if DEBUG
[assembly:AssemblyConfiguration("Debug version")]
#else
[assembly:AssemblyConfiguration("Release version")]
#endif
[assembly:ComVisible(false)]

[assembly:AssemblyVersion("1.0.0.0")]


[assembly: SuppressMessage("Style", "IDE1006:Naming Styles")]

