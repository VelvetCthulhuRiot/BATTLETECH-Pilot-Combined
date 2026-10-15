using System;
using System.Reflection;

class Probe2
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";
    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        asm = Assembly.LoadFrom(AsmPath);

        Base("BattleTech.UI.HBSToggle");
        Base("BattleTech.UI.HBSSliderInput");
        Base("BattleTech.UI.HBSRadioButton");
        Base("BattleTech.UI.HBSDOTweenToggle");

        Console.WriteLine("--- HBSSliderInput.OnChangeEvent ---");
        Type ce = Find("BattleTech.UI.HBSSliderInput+OnChangeEvent");
        if (ce == null) ce = Find("OnChangeEvent");
        if (ce != null) { Console.WriteLine("  " + ce.FullName + " base=" + B(ce.BaseType)); }
        else Console.WriteLine("  (not found)");

        Console.WriteLine("--- 找带 SetToggled/拖拽的 UI 组件 ---");
        foreach (string n in new string[] {
            "BattleTech.UI.HBSToggle", "BattleTech.UI.HBSSliderInput",
            "BattleTech.UI.HBSRadioButton", "BattleTech.UI.HBSButton" })
        {
            Type t = Find(n);
            if (t == null) { Console.WriteLine("  " + n + " 不存在"); continue; }
            Console.WriteLine("  " + n + " : base=" + B(t.BaseType));
        }
    }

    static void Base(string full)
    {
        Type t = Find(full);
        if (t == null) { Console.WriteLine("!! " + full); return; }
        Console.WriteLine(full + "  base=" + B(t.BaseType) + "  isUnityToggle=" + (t.BaseType != null && t.BaseType.FullName == "UnityEngine.UI.Toggle"));
    }

    static string B(Type t) { return t == null ? "(none)" : t.FullName; }

    static Type Find(string full)
    {
        Type t = asm.GetType(full, false);
        if (t != null) return t;
        foreach (Type x in asm.GetTypes()) if (x.Name == full || x.FullName == full) return x;
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
