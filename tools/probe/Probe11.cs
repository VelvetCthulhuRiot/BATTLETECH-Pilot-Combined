using System;
using System.Reflection;
using System.Collections.Generic;

class Probe11
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine"); Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule"); Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== HBSSliderInput / HBSSliderHandle 的数值字段类型（决定 1% 步长可不可行）===");
        foreach (string tn in new string[] { "BattleTech.UI.HBSSliderInput", "BattleTech.UI.HBSSliderHandle" })
        {
            Type t = asm.GetType(tn);
            Console.WriteLine("  [" + tn + "]");
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string n = f.Name.ToLower();
                if (n.Contains("value") || n.Contains("step") || n.Contains("limit") || n.Contains("min") || n.Contains("max"))
                    Console.WriteLine("      " + TN(f.FieldType) + "  " + f.Name);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== HBSButton 的视觉/状态 API ===");
        Type hb = asm.GetType("BattleTech.UI.HBSButton");
        foreach (PropertyInfo p in hb.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Console.WriteLine("  P " + p.Name + " : " + TN(p.PropertyType));
        foreach (FieldInfo f in hb.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            Console.WriteLine("  F " + f.Name + " : " + TN(f.FieldType));

        Console.WriteLine();
        Console.WriteLine("=== UIColorRefTracker 的用法 ===");
        Type uc = asm.GetType("BattleTech.UI.UIColorRefTracker");
        if (uc != null)
            foreach (MethodInfo m in uc.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                ParameterInfo[] ps = m.GetParameters();
                string[] a = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++) a[i] = TN(ps[i].ParameterType) + " " + ps[i].Name;
                Console.WriteLine("  " + m.Name + "(" + string.Join(", ", a) + ")");
            }

        Console.WriteLine();
        Console.WriteLine("=== 有没有现成的\"窗口/面板底\"PrefabName 候选 ===");
        foreach (Type t in SafeTypes())
        {
            if (!t.IsPublic) continue;
            string pn = PrefabName(t);
            if (pn == "(无)") continue;
            string low = pn.ToLower();
            if (low.Contains("popuppanel") || low.Contains("window") || low.Contains("genericpopup")
                || low.Contains("tooltip") || low.Contains("modalpanel"))
                Console.WriteLine("  " + pn + "   (" + t.Name + ")");
        }
    }

    static string PrefabName(Type t)
    {
        foreach (object a in t.GetCustomAttributes(false))
        {
            PropertyInfo pi = a.GetType().GetProperty("PrefabName");
            if (pi != null) return pi.GetValue(a, null) as string;
        }
        return "(无)";
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
        return t.Name;
    }

    static Assembly Resolve(object s, ResolveEventArgs a) { return Preload(new AssemblyName(a.Name).Name); }
    static Assembly Preload(string n)
    {
        try
        {
            string f = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AsmPath), n + ".dll");
            if (System.IO.File.Exists(f)) return Assembly.LoadFrom(f);
        }
        catch { }
        return null;
    }
}
