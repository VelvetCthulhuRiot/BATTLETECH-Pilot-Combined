using System;
using System.Reflection;

class Probe
{
    const string AsmPath = @"X:\path\to\BATTLETECH\BattleTech_Data\Managed\Assembly-CSharp.dll";

    static Assembly asm;

    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        asm = Assembly.LoadFrom(AsmPath);
        Console.WriteLine("assembly: " + asm.FullName + "  types=" + asm.GetTypes().Length);
        Dump("BattleTech.UI.GenericPopupBuilder", "AddInput");
        Dump("BattleTech.UI.GenericPopupBuilder", "AddButton");
        Dump("BattleTech.UI.GenericPopupBuilder", "SetOnClose");
        Dump("BattleTech.UI.GenericPopupBuilder", "AddFader");
        Dump("BattleTech.UI.GenericPopupBuilder", "Create");
        Dump("BattleTech.UI.GenericPopup", "ShowInput");
        Dump("BattleTech.Data.DataManager", "PrecachePrefab");
        Dump("BattleTech.Data.DataManager", "PooledInstantiate");
        Dump("BattleTech.UI.HBSSliderInput", "SetValue");
        Dump("BattleTech.UI.HBSSliderInput", "SetLimits");
        Dump("BattleTech.UI.HBSSliderInput", "SetBounds");
        Dump("BattleTech.UI.HBSSliderInput", "set_Value");
        DumpField("BattleTech.UI.HBSSliderInput", "_value");
        DumpField("BattleTech.UI.HBSSliderInput", "OnValueChanged");
        DumpField("BattleTech.UI.HBSSliderInput", "step");
        DumpField("BattleTech.UI.HBSSliderInput", "handle");
        DumpField("BattleTech.UI.GenericPopup", "_contentInputContainer");
        DumpField("BattleTech.UI.GenericPopup", "_buttonLayout");
    }

    static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(AsmPath);
            string file = System.IO.Path.Combine(dir, new AssemblyName(args.Name).Name + ".dll");
            if (System.IO.File.Exists(file)) return Assembly.LoadFrom(file);
        }
        catch { }
        return null;
    }

    static Type Get(string full)
    {
        Type t = asm.GetType(full, false);
        if (t != null) return t;
        foreach (Type x in asm.GetTypes())
        {
            if (x.FullName == full || x.Name == full) return x;
        }
        return null;
    }

    static void Dump(string type, string name)
    {
        Type t = Get(type);
        if (t == null) { Console.WriteLine("!! type not found: " + type); return; }
        MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static);
        bool any = false;
        foreach (MethodInfo m in ms)
        {
            if (m.Name != name) continue;
            any = true;
            ParameterInfo[] ps = m.GetParameters();
            string[] parts = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                parts[i] = TypeName(ps[i].ParameterType) + " " + ps[i].Name;
            }
            Console.WriteLine(t.Name + "." + m.Name + " -> " + TypeName(m.ReturnType) +
                              " (" + string.Join(", ", parts) + ")");
        }
        if (!any) Console.WriteLine("!! method not found: " + type + "." + name);
    }

    static void DumpField(string type, string name)
    {
        Type t = Get(type);
        if (t == null) { Console.WriteLine("!! type not found: " + type); return; }
        FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.Instance | BindingFlags.Static);
        if (f == null) { Console.WriteLine("!! field not found: " + type + "." + name); return; }
        Console.WriteLine(t.Name + "." + f.Name + " : " + TypeName(f.FieldType));
    }

    static string TypeName(Type t)
    {
        if (t == null) return "?";
        if (t.IsGenericType)
        {
            string n = t.Name;
            int tick = n.IndexOf('`');
            if (tick > 0) n = n.Substring(0, tick);
            Type[] args = t.GetGenericArguments();
            string[] parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++) parts[i] = TypeName(args[i]);
            return n + "<" + string.Join(", ", parts) + ">";
        }
        if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
        return t.Name;
    }
}
