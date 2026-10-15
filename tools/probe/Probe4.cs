using System;
using System.Collections.Generic;
using System.Reflection;

class Probe4
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== 所有名字里带 Slider 的类型 ===");
        foreach (Type t in asm.GetTypes())
        {
            if (!t.IsPublic && !t.IsNestedPublic) continue;
            if (t.Name.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("  " + t.FullName + "  base=" + B(t.BaseType) + "  ifaces=" + Ifaces(t));
        }

        Console.WriteLine();
        Console.WriteLine("=== HBSSliderInput 实现的接口（决定拖拽事件谁来收）===");
        Show("BattleTech.UI.HBSSliderInput");
        Show("BattleTech.UI.HBSSliderHandle");
        Show("BattleTech.UI.HBSToggle");
        Show("BattleTech.UI.HBSButtonBase");
        Show("BattleTech.UI.HBSButton");

        Console.WriteLine();
        Console.WriteLine("=== SliderInput 的某个子类可能就是预制体上的组件 ===");
        foreach (Type t in asm.GetTypes())
        {
            if (t.BaseType != null && t.BaseType.Name == "HBSSliderInput")
                Console.WriteLine("  " + t.FullName + " : HBSSliderInput  ifaces=" + Ifaces(t));
        }

        Console.WriteLine();
        Console.WriteLine("=== 找 Slider 相关的 HBS 组件（按 PrefabName 属性）===");
        foreach (Type t in asm.GetTypes())
        {
            object[] attrs = t.GetCustomAttributes(false);
            foreach (object a in attrs)
            {
                string an = a.GetType().Name;
                if (an.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PropertyInfo pn = a.GetType().GetProperty("PrefabName");
                    if (pn != null)
                    {
                        string v = pn.GetValue(a, null) as string;
                        if (v != null && v.IndexOf("Slid", StringComparison.OrdinalIgnoreCase) >= 0)
                            Console.WriteLine("  " + t.FullName + "  PrefabName=" + v);
                    }
                    break;
                }
            }
        }
    }

    static void Show(string full)
    {
        Type t = Find(full);
        if (t == null) { Console.WriteLine("  !! " + full); return; }
        Console.WriteLine("  " + t.FullName + "  base=" + B(t.BaseType));
        Console.WriteLine("     ifaces: " + Ifaces(t));
    }

    static string Ifaces(Type t)
    {
        Type[] i = t.GetInterfaces();
        if (i.Length == 0) return "(none)";
        List<string> names = new List<string>();
        foreach (Type x in i) names.Add(x.Name);
        return string.Join(", ", names.ToArray());
    }

    static string B(Type t) { return t == null ? "(none)" : t.FullName; }

    static Type Find(string full)
    {
        Type t = asm.GetType(full, false);
        if (t != null) return t;
        foreach (Type x in asm.GetTypes()) if (x.FullName == full || x.Name == full) return x;
        return null;
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
