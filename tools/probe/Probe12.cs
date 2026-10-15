using System;
using System.Reflection;
using System.Collections.Generic;

class Probe12
{
    const string AsmPath = @"D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine"); Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule"); Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== ModLoader 与 mod AssetBundle 相关的成员 ===");
        Type ml = asm.GetType("BattleTech.ModSupport.ModLoader");
        foreach (FieldInfo f in ml.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            if (f.Name.IndexOf("Bundle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.Name.IndexOf("BTRL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.Name.IndexOf("Manifest", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("  F " + TN(f.FieldType) + "  " + f.Name);
        foreach (MethodInfo m in ml.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (m.IsSpecialName) continue;
            if (m.Name.IndexOf("Bundle", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.Name.IndexOf("BTRL", StringComparison.OrdinalIgnoreCase) < 0 &&
                m.Name.IndexOf("Manifest", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("  M " + Sig(m));
        }

        Console.WriteLine();
        Console.WriteLine("=== ModDef 的字段（看 mod.json 支持哪些键，尤其是 AssetBundle / Manifest）===");
        Type md = asm.GetType("BattleTech.ModSupport.ModDef");
        if (md == null) { Console.WriteLine("  !! 找不到 ModDef"); }
        else foreach (FieldInfo f in md.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Console.WriteLine("  F " + TN(f.FieldType) + "  " + f.Name);

        Console.WriteLine();
        Console.WriteLine("=== VersionManifestEntry 的字段（Manifest 条目长什么样）===");
        Type vme = asm.GetType("BattleTech.Data.VersionManifestEntry");
        if (vme == null) Console.WriteLine("  !! 找不到");
        else foreach (FieldInfo f in vme.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Console.WriteLine("  F " + TN(f.FieldType) + "  " + f.Name);

        Console.WriteLine();
        Console.WriteLine("=== ResourceLocator / 找 AssetBundle 路径的 API ===");
        foreach (Type t in SafeTypes())
        {
            if (!t.IsPublic) continue;
            if (t.Name.IndexOf("ResourceLocator", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("  [" + t.FullName + "]");
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (m.IsSpecialName) continue;
                Console.WriteLine("     " + Sig(m));
            }
        }
    }

    static string Sig(MethodInfo m)
    {
        ParameterInfo[] ps = m.GetParameters();
        string[] a = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++) a[i] = TN(ps[i].ParameterType) + " " + ps[i].Name;
        return m.Name + "(" + string.Join(", ", a) + ") -> " + TN(m.ReturnType) + (m.IsStatic ? " [static]" : "");
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
        if (t.IsArray) return TN(t.GetElementType()) + "[]";
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
