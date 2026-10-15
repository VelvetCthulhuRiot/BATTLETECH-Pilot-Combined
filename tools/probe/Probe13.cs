using System;
using System.Reflection;
using System.Collections.Generic;

class Probe13
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine"); Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule"); Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== 名字里含 ModDef / ModEntry / VersionManifestEntry 的类型 ===");
        foreach (Type t in SafeTypes())
        {
            string n = t.Name;
            if (n == "GameModDef" || n == "SystemModDef" || n == "ModEntry" || n == "BaseModDef"
                || n == "VersionManifestEntry" || n == "ModDef")
                Console.WriteLine("  " + t.FullName + "  (base=" + (t.BaseType == null ? "?" : t.BaseType.Name) + ")");
        }

        foreach (string full in new string[] {
            "BattleTech.ModSupport.GameModDef", "BattleTech.ModSupport.BaseModDef",
            "BattleTech.ModSupport.ModEntry", "BattleTech.Data.VersionManifestEntry" })
        {
            Type t = asm.GetType(full, false);
            if (t == null) { Console.WriteLine("!! 找不到 " + full); continue; }
            Console.WriteLine();
            Console.WriteLine("=== " + t.FullName + " 字段 ===");
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Console.WriteLine("  F " + TN(f.FieldType) + "  " + f.Name);
            Console.WriteLine("  -- 属性 --");
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Console.WriteLine("  P " + TN(p.PropertyType) + "  " + p.Name);
        }

        Console.WriteLine();
        Console.WriteLine("=== ExpandManifest 的 IL（看 mod Manifest 条目怎么被解析）===");
        Type ml = asm.GetType("BattleTech.ModSupport.ModLoader");
        MethodInfo em = ml.GetMethod("ExpandManifest", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (em != null) DumpStrings(em);
    }

    static void DumpStrings(MethodInfo m)
    {
        MethodBody body = m.GetMethodBody();
        if (body == null) return;
        byte[] il = body.GetILAsByteArray();
        Module mod = m.Module;
        for (int i = 0; i < il.Length; i++)
        {
            byte op = il[i];
            if (op != 0x72 && op != 0x28 && op != 0x6F && op != 0x7B) continue;
            if (i + 4 >= il.Length) break;
            int tok = BitConverter.ToInt32(il, i + 1);
            try
            {
                if (op == 0x72)
                {
                    string s = mod.ResolveString(tok);
                    if (s != null && s.Length < 100) Console.WriteLine("    0x" + i.ToString("x4") + "  ldstr \"" + s + "\"");
                }
                else
                {
                    MemberInfo mi = mod.ResolveMember(tok);
                    if (mi != null) Console.WriteLine("    0x" + i.ToString("x4") + "  " + mi.DeclaringType.Name + "::" + mi.Name);
                }
            }
            catch { }
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
