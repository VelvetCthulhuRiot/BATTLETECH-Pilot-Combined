using System;
using System.Reflection;

class Probe5
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        // 先显式把 Unity 的几个模块加载进来，否则 asm.GetTypes() 会因为
        // "类型已转发到 UnityEngine.UIModule" 而抛异常。
        Preload("UnityEngine");
        Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule");
        Preload("UnityEngine.UI");
        Preload("UnityEngine.TextRenderingModule");
        asm = Assembly.LoadFrom(AsmPath);

        Dump("BattleTech.UI.UIModule", "get_SortBehavior");
        Dump("BattleTech.UI.UIModule", "set_SortBehavior");
        Dump("BattleTech.UI.UIModule", "get_PrefabName");
        Dump("BattleTech.UI.UIModule", "SetPrefabName");
        Dump("BattleTech.UI.UIModule", "Init");

        Console.WriteLine();
        Console.WriteLine("--- SortBehavior 的类型 ---");
        Type uim = Find("BattleTech.UI.UIModule");
        PropertyInfo pi = uim.GetProperty("SortBehavior", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (pi != null)
        {
            Console.WriteLine("  SortBehavior : " + pi.PropertyType.FullName);
            if (pi.PropertyType.IsEnum)
            {
                foreach (string n in Enum.GetNames(pi.PropertyType)) Console.WriteLine("      " + n);
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- UIRoot / 顶层 UI 类 ---");
        foreach (Type t in asm.GetTypes())
        {
            if (!t.IsPublic) continue;
            string n = t.Name;
            if (n == "UIRoot" || n == "UIManager" || n.IndexOf("UIRoot", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("UICanvas", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("  " + t.FullName + "  base=" + (t.BaseType == null ? "?" : t.BaseType.Name));
        }

        Console.WriteLine();
        Console.WriteLine("--- 找带 Canvas 引用的 HBS 类 ---");
        foreach (Type t in asm.GetTypes())
        {
            if (!t.IsPublic) continue;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(UnityEngine.Canvas) || f.FieldType == typeof(UnityEngine.CanvasGroup))
                {
                    Console.WriteLine("  " + t.FullName + "." + f.Name + " : " + f.FieldType.Name);
                    break;
                }
            }
        }
    }

    static void Dump(string type, string name)
    {
        Type t = Find(type);
        if (t == null) { Console.WriteLine("!! " + type); return; }
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.Name != name) continue;
            ParameterInfo[] ps = m.GetParameters();
            string[] a = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) a[i] = ps[i].ParameterType.Name + " " + ps[i].Name;
            Console.WriteLine(t.Name + "." + m.Name + "(" + string.Join(", ", a) + ") -> " + m.ReturnType.Name);
        }
    }

    static Type Find(string full)
    {
        Type t = asm.GetType(full, false);
        if (t != null) return t;
        foreach (Type x in asm.GetTypes()) if (x.FullName == full || x.Name == full) return x;
        return null;
    }

    static Assembly Resolve(object s, ResolveEventArgs a)
    {
        return Preload(new AssemblyName(a.Name).Name);
    }

    static Assembly Preload(string name)
    {
        try
        {
            string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AsmPath), name + ".dll");
            if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
        }
        catch { }
        return null;
    }
}
