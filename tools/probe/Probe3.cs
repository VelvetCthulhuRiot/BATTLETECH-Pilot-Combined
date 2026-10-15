using System;
using System.Reflection;

class Probe3
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        asm = Assembly.LoadFrom(AsmPath);

        Dump("BattleTech.GameInstance", "Update");
        Dump("BattleTech.SimGameState", "Update");
        Dump("BattleTech.GameInstance", "get_Simulation");
        Dump("BattleTech.GameInstance", "get_Combat");
        Dump("BattleTech.UI.GenericPopupBuilder", "Render");
        Dump("BattleTech.UI.GenericPopupBuilder", "SetAlwaysOnTop");
        Dump("BattleTech.UI.UIModule", "ForceRefreshImmediate");
        Dump("BattleTech.UI.UIModule", "GetPrefab");
        Dump("BattleTech.UI.UIModule", "Instantiate");

        Console.WriteLine("--- UIModule 所有方法 ---");
        Type uim = Find("BattleTech.UI.UIModule");
        if (uim != null)
        {
            foreach (MethodInfo m in uim.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.Name.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Name.IndexOf("Instantiate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.Name.IndexOf("Refresh", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("  " + Sig(m));
            }
        }
    }

    static string Sig(MethodInfo m)
    {
        ParameterInfo[] ps = m.GetParameters();
        string[] parts = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++) parts[i] = TypeName(ps[i].ParameterType) + " " + ps[i].Name;
        return m.Name + " -> " + TypeName(m.ReturnType) + " (" + string.Join(", ", parts) + ")  static=" + m.IsStatic;
    }

    static void Dump(string type, string name)
    {
        Type t = Find(type);
        if (t == null) { Console.WriteLine("!! " + type); return; }
        bool any = false;
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            if (m.Name != name) continue;
            any = true;
            Console.WriteLine(t.Name + "." + Sig(m));
        }
        if (!any) Console.WriteLine("!! " + type + "." + name + " 不存在");
    }

    static Type Find(string full)
    {
        Type t = asm.GetType(full, false);
        if (t != null) return t;
        foreach (Type x in asm.GetTypes()) if (x.FullName == full || x.Name == full) return x;
        return null;
    }

    static string TypeName(Type t)
    {
        if (t == null) return "?";
        if (t.IsGenericType)
        {
            string n = t.Name; int tick = n.IndexOf('`'); if (tick > 0) n = n.Substring(0, tick);
            Type[] a = t.GetGenericArguments(); string[] p = new string[a.Length];
            for (int i = 0; i < a.Length; i++) p[i] = TypeName(a[i]);
            return n + "<" + string.Join(", ", p) + ">";
        }
        if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
        return t.Name;
    }

    static Assembly Resolve(object s, ResolveEventArgs a)
    {
        try
        {
            string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AsmPath), new AssemblyName(a.Name).Name + ".dll");
            if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
        }
        catch { }
        return null;
    }
}
