using System;
using System.Linq;
using System.Reflection;

class P {
  static void Dump(Type t) {
    if (t == null) { Console.WriteLine("TYPE NULL"); return; }
    Console.WriteLine("=== " + t.FullName + " ===");
    foreach (var m in t.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).OrderBy(x=>x.Name)) {
      if (m.IsPrivate && !m.IsFamily) continue;
      string mods = (m.IsFamily?"protected ":"") + (m.IsPublic?"public ":"") + (m.IsVirtual?"virtual ":"") + (m.IsAbstract?"abstract ":"");
      Console.WriteLine("  " + mods + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name)) + ")");
    }
    foreach (var c in t.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
      Console.WriteLine("  ctor(" + string.Join(", ", c.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name)) + ")");
  }
  static void Main() {
    var asm = typeof(Xunit.Sdk.XunitTestInvoker).Assembly;
    Console.WriteLine("ASM: " + asm.FullName);
    Dump(typeof(Xunit.Sdk.XunitTestInvoker));
    Dump(typeof(Xunit.Sdk.XunitTestRunner));
    Dump(typeof(Xunit.Sdk.TestInvoker<>));
    Dump(typeof(Xunit.Sdk.XunitTest));
    Dump(typeof(Xunit.FactAttribute).BaseType);
  }
}
