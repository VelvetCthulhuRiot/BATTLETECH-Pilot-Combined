using System;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// 找出 HBSSliderInput 真正需要哪些子物体。
/// 不实例化预制体（那样需要 Unity 运行时），而是读 HBSSliderInput 自己的方法体，
/// 看它去 GetComponent / GetComponentInChildren 找什么。
/// </summary>
class Probe7
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

        Console.WriteLine("=== HBSSliderInput 的字段类型（决定它需要哪些子物体）===");
        Type t = asm.GetType("BattleTech.UI.HBSSliderInput");
        foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Console.WriteLine("  " + (f.IsPublic ? "public  " : "protected") + " " + TN(f.FieldType) + " " + f.Name);
        }

        Console.WriteLine();
        Console.WriteLine("=== HBSSliderInput.Awake 里 GetComponent* 的调用（f 是泛型参数类型）===");
        MethodInfo awake = t.GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (awake != null) DumpCalls(awake);

        Console.WriteLine();
        Console.WriteLine("=== 同屏幕里 SGContractsWidget 用的两个滑块字段类型确认 ===");
        Type w = asm.GetType("BattleTech.UI.SGContractsWidget");
        foreach (FieldInfo f in w.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (f.Name.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0)
                Console.WriteLine("  " + TN(f.FieldType) + " " + f.Name);
        }
    }

    /// <summary>粗暴但有效：扫 IL，找 call/callvirt 到 GetComponent 系列的地方，打印附近信息。</summary>
    static void DumpCalls(MethodInfo m)
    {
        try
        {
            MethodBody body = m.GetMethodBody();
            if (body == null) { Console.WriteLine("  (无方法体)"); return; }
            byte[] il = body.GetILAsByteArray();
            Console.WriteLine("  IL 长度 " + il.Length + " 字节");

            // 只打印出现的字符串与类型 token 名字，够用了
            Module mod = m.Module;
            for (int i = 0; i < il.Length; i++)
            {
                byte op = il[i];
                if (op == 0x72 || op == 0x73 || op == 0x28 || op == 0x6F)
                {
                    if (i + 4 >= il.Length) break;
                    int tok = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        if (op == 0x72)
                        {
                            string s = mod.ResolveString(tok);
                            if (s != null && s.Length < 120) Console.WriteLine("    0x" + i.ToString("x4") + " ldstr " + s);
                        }
                        else
                        {
                            MemberInfo mi = mod.ResolveMember(tok);
                            if (mi != null && mi.Name.IndexOf("Component", StringComparison.OrdinalIgnoreCase) >= 0)
                                Console.WriteLine("    0x" + i.ToString("x4") + " " + mi.DeclaringType.Name + "::" + mi.Name);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception e) { Console.WriteLine("  !! " + e.Message); }
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
