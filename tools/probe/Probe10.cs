using System;
using System.Reflection;
using System.Collections.Generic;

class Probe10
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine"); Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule"); Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== HBSToggle 的状态到底存在哪 ===");
        Body("BattleTech.UI.HBSToggle", "IsChecked");
        Body("BattleTech.UI.HBSToggle", "IsToggled");
        Body("BattleTech.UI.HBSToggle", "SetToggled");
        Body("BattleTech.UI.HBSToggle", "OnPointerUp");

        Console.WriteLine();
        Console.WriteLine("=== HBSToggle 自身 + 继承链上的所有字段 ===");
        Type t = asm.GetType("BattleTech.UI.HBSToggle");
        while (t != null && t.Name != "MonoBehaviour" && t.Name != "Object")
        {
            Console.WriteLine("  [" + t.FullName + "]");
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                Console.WriteLine("      " + (f.IsPublic ? "public " : "nonpub ") + TN(f.FieldType) + "  " + f.Name);
            t = t.BaseType;
        }

        Console.WriteLine();
        Console.WriteLine("=== ButtonState 枚举 ===");
        Type bs = asm.GetType("BattleTech.UI.ButtonState");
        if (bs != null) foreach (string n in Enum.GetNames(bs)) Console.WriteLine("      " + n);
    }

    /// <summary>反汇编一个方法体，把 ldstr / 调用的成员 / 字段访问列出来。</summary>
    static void Body(string typeName, string methodName)
    {
        Type t = asm.GetType(typeName);
        if (t == null) { Console.WriteLine("!! " + typeName); return; }
        MethodInfo m = t.GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (m == null) { Console.WriteLine("!! " + typeName + "." + methodName); return; }

        Console.WriteLine("  --- " + typeName + "." + methodName + " -> " + TN(m.ReturnType) + " ---");
        MethodBody body = m.GetMethodBody();
        if (body == null) { Console.WriteLine("      (无方法体)"); return; }
        byte[] il = body.GetILAsByteArray();
        Module mod = m.Module;
        for (int i = 0; i < il.Length; i++)
        {
            byte op = il[i];
            if (op != 0x28 && op != 0x6F && op != 0x7B && op != 0x7D && op != 0x7E && op != 0x80 && op != 0x72) continue;
            if (i + 4 >= il.Length) break;
            int tok = BitConverter.ToInt32(il, i + 1);
            try
            {
                if (op == 0x72)
                {
                    string s = mod.ResolveString(tok);
                    Console.WriteLine("      0x" + i.ToString("x4") + "  ldstr \"" + s + "\"");
                }
                else
                {
                    MemberInfo mi = mod.ResolveMember(tok);
                    if (mi != null)
                        Console.WriteLine("      0x" + i.ToString("x4") + "  " + (mi is FieldInfo ? "F " : "M ")
                            + mi.DeclaringType.Name + "::" + mi.Name);
                }
            }
            catch { }
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
