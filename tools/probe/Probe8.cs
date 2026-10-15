using System;
using System.Reflection;
using System.Collections.Generic;

class Probe8
{
    const string AsmPath = @"D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine");
        Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule");
        Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Prefab("BattleTech.UI.SettingsMenu");
        Prefab("BattleTech.UI.SettingsMenuVideo");
        Prefab("BattleTech.UI.SettingsMenuAudio");
        Prefab("BattleTech.UI.SettingsMenuGameplay");
        Prefab("BattleTech.UI.SettingsMenuControls");

        Console.WriteLine();
        Console.WriteLine("=== 哪些类有 HBSSliderInput 字段（前面已知 8 处）+ 它们的 PrefabName ===");
        Type si = asm.GetType("BattleTech.UI.HBSSliderInput");
        Dictionary<Type, int> owners = new Dictionary<Type, int>();
        foreach (Type t in SafeTypes())
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType == si)
                {
                    if (!owners.ContainsKey(t)) owners[t] = 0;
                    owners[t]++;
                }
            }
        }
        foreach (KeyValuePair<Type, int> kv in owners)
            Console.WriteLine("  " + kv.Key.FullName + "  (" + kv.Value + " 个 slider)  PrefabName=" + PrefabName(kv.Key));

        Console.WriteLine();
        Console.WriteLine("=== 哪些类有 HBSToggle / UnityEngine.UI.Toggle 字段 ===");
        foreach (Type t in SafeTypes())
        {
            if (!t.IsPublic) continue;
            int nSvg = 0, nUi = 0;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.Name.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.Name.IndexOf("Checkbox", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("  " + t.FullName + "." + f.Name + " : " + TN(f.FieldType));
                }
            }
            if (nSvg + nUi > 0) { }
        }

        Console.WriteLine();
        Console.WriteLine("=== SettingsMenu 类里所有 Slider/Toggle 字段 ===");
        Type sm = asm.GetType("BattleTech.UI.SettingsMenu");
        if (sm != null)
        {
            foreach (FieldInfo f in sm.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string n = f.Name.ToLower();
                if (n.Contains("slid") || n.Contains("toggle") || n.Contains("check") || n.Contains("option"))
                    Console.WriteLine("  " + TN(f.FieldType) + "  " + f.Name);
            }
            Console.WriteLine("  --- 方法 ---");
            foreach (MethodInfo m in sm.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                ParameterInfo[] ps = m.GetParameters();
                string[] a = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++) a[i] = TN(ps[i].ParameterType) + " " + ps[i].Name;
                Console.WriteLine("  " + m.Name + "(" + string.Join(", ", a) + ")");
            }
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

    static void Prefab(string full)
    {
        Type t = asm.GetType(full, false);
        Console.WriteLine("  " + full + "  PrefabName=" + (t == null ? "!! 找不到" : PrefabName(t)));
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
