using System;
using System.Reflection;
using System.Collections.Generic;

class Probe9
{
    const string AsmPath = @"D:\MyDownload\Things\Steam\steamapps\common\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        Preload("UnityEngine"); Preload("UnityEngine.CoreModule");
        Preload("UnityEngine.UIModule"); Preload("UnityEngine.UI");
        asm = Assembly.LoadFrom(AsmPath);

        Console.WriteLine("=== 设置菜单里那些开关到底是什么类型 ===");
        foreach (string n in new string[] {
            "BattleTech.UI.GameSettingsModule", "BattleTech.UI.AudioSettingsModule",
            "BattleTech.UI.VideoSettingsModule", "BattleTech.UI.ControlSettingsModule" })
        {
            Type t = asm.GetType(n, false);
            if (t == null) { Console.WriteLine("  !! " + n); continue; }
            Console.WriteLine("  " + n + "  PrefabName=" + PrefabName(t));
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.Name.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    f.Name.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("      " + TN(f.FieldType) + "  " + f.Name);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== 哪些类型带 PrefabName 且含有 Toggle（找简单的勾选框预制体）===");
        foreach (Type t in SafeTypes())
        {
            if (!t.IsPublic) continue;
            string pn = PrefabName(t);
            if (pn == "(无)") continue;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string tn = f.FieldType.Name;
                if (tn == "HBSToggle" || tn == "HBSDOTweenToggle" || tn == "Toggle")
                {
                    Console.WriteLine("  " + pn + "   (" + t.Name + "." + f.Name + " : " + tn + ")");
                    break;
                }
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

    static string TN(Type t) { return t == null ? "?" : t.Name; }

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
