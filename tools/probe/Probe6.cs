using System;
using System.Reflection;
using System.Collections.Generic;

class Probe6
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine");
        Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule");
        Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        DumpType("UIWidgets.RangeSlider");
        DumpType("UIWidgets.RangeSliderFloat");
        DumpType("UIWidgets.RangeSliderBase`1");
        DumpType("UIWidgets.RangeSliderHandle");
        DumpType("UIWidgets.RangeSliderSample");
        DumpType("UIWidgets.RangeSliderFloatSample");
        DumpType("UIWidgets.CenteredSlider");

        Console.WriteLine();
        Console.WriteLine("=== 哪些类型带 PrefabName 且名字里含 Slid/Slidr ===");
        foreach (Type t in SafeTypes())
        {
            if (!t.IsPublic) continue;
            foreach (object a in t.GetCustomAttributes(false))
            {
                PropertyInfo pn = a.GetType().GetProperty("PrefabName");
                if (pn == null) continue;
                string v = pn.GetValue(a, null) as string;
                if (v != null && v.ToLower().Contains("slid"))
                    Console.WriteLine("  " + t.FullName + "  PrefabName=" + v);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 谁真的引用 HBSSliderInput？（找它的使用者，反推预制体）===");
        Type sliderInput = asm.GetType("BattleTech.UI.HBSSliderInput");
        Console.WriteLine("  HBSSliderInput 存在: " + (sliderInput != null));
        if (sliderInput != null)
        {
            foreach (Type t in SafeTypes())
            {
                if (!t.IsPublic) continue;
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (f.FieldType == sliderInput)
                        Console.WriteLine("  " + t.FullName + "." + f.Name);
                }
            }
        }
    }

    static IEnumerable<Type> SafeTypes()
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            List<Type> ok = new List<Type>();
            foreach (Type t in e.Types) if (t != null) ok.Add(t);
            return ok;
        }
    }

    static void DumpType(string full)
    {
        Type t = asm.GetType(full, false);
        if (t == null) { Console.WriteLine("!! 找不到 " + full); return; }
        Console.WriteLine();
        Console.WriteLine("=== " + t.FullName + "  base=" + (t.BaseType == null ? "?" : t.BaseType.Name) + " ===");
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine("  P " + p.Name + " : " + TN(p.PropertyType)
                + (p.CanRead ? " get" : "") + (p.CanWrite ? " set" : ""));
        }
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine("  F " + f.Name + " : " + TN(f.FieldType) + (f.IsPublic ? " [public]" : " [nonpublic]"));
        }
        foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (m.IsSpecialName) continue;
            ParameterInfo[] ps = m.GetParameters();
            string[] a = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) a[i] = TN(ps[i].ParameterType) + " " + ps[i].Name;
            Console.WriteLine("  M " + m.Name + "(" + string.Join(", ", a) + ") -> " + TN(m.ReturnType) + (m.IsPublic ? " [public]" : " [nonpublic]"));
        }
    }

    static string TN(Type t)
    {
        if (t == null) return "?";
        if (t.IsGenericType)
        {
            string n = t.Name; int i = n.IndexOf('`'); if (i > 0) n = n.Substring(0, i);
            Type[] a = t.GetGenericArguments(); string[] p = new string[a.Length];
            for (int k = 0; k < a.Length; k++) p[k] = TN(a[k]);
            return n + "<" + string.Join(", ", p) + ">";
        }
        if (t.IsArray) return TN(t.GetElementType()) + "[]";
        return t.Name;
    }

    static Assembly Resolve(object s, ResolveEventArgs a) { return Preload(new AssemblyName(a.Name).Name); }

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
