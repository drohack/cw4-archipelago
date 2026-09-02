// Search CW4's IL2CPP interop assembly for a member by name, without launching
// the game.
//
//     dotnet run --project tools/reflect -- GetEff
//     dotnet run --project tools/reflect -- MYRANGE
//     dotnet run --project tools/reflect -- Cannon type
//
// Prints the DECLARING TYPE and whether each hit is static, which is the part
// guesswork gets wrong. Two real examples, both of which had resisted an
// afternoon of guessing and fell out of one command here:
//
//   - ERNInterface has BOTH GetEff(int) (instance, feeds the UI) and
//     GetEfficiency(int) (STATIC, what the sim reads). Patching only the first
//     produced an upgrade that moved every number a probe could read and
//     nothing in the game.
//   - MYRANGE is declared per weapon type (Cannon, Mortar, Sniper, ...) and not
//     on UnitManager, so there is no base-class property to read.
//
// Metadata-only via MetadataLoadContext: nothing is executed, so the interop
// assemblies' dependency cascade is irrelevant. Do not reach for
// Assembly.LoadFrom in PowerShell instead - that stack-overflows on the resolve
// recursion.
using System;
using System.IO;
using System.Linq;
using System.Reflection;

class P {
    static void Main(string[] a) {
        string interop = @"G:\Games\Steam\steamapps\common\Creeper World 4\BepInEx\interop";
        string core    = @"G:\Games\Steam\steamapps\common\Creeper World 4\BepInEx\core";
        var files = Directory.GetFiles(interop, "*.dll").ToList();
        files.AddRange(Directory.GetFiles(core, "*.dll"));
        var rt = Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll");
        files.AddRange(rt);
        var res = new PathAssemblyResolver(files.Distinct());
        using var ctx = new MetadataLoadContext(res);
        var asm = ctx.LoadFromAssemblyPath(Path.Combine(interop, "Assembly-CSharp.dll"));
        string needle = a.Length > 0 ? a[0] : "GetEfficiency";
        string mode = a.Length > 1 ? a[1] : "method";
        foreach (var t in asm.GetTypes()) {
            try {
                if (mode == "ctor") {
                    if (t.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
                        var ps = string.Join(",", c.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name));
                        Console.WriteLine($"CTOR {t.FullName}({ps})");
                    }
                    continue;
                }
                if (mode == "type") {
                    if (t.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        Console.WriteLine("TYPE " + t.FullName);
                    continue;
                }
                var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                       | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                foreach (var m in t.GetMethods(bf)) {
                    if (m.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ps = string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name));
                    Console.WriteLine($"{(m.IsStatic ? "STATIC " : "       ")}{t.FullName}.{m.Name}({ps}) -> {m.ReturnType.Name}");
                }
                foreach (var f in t.GetFields(bf)) {
                    if (f.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Console.WriteLine($"{(f.IsStatic ? "SFIELD " : "FIELD  ")}{t.FullName}.{f.Name} : {f.FieldType.Name}");
                }
                foreach (var pr in t.GetProperties(bf)) {
                    if (pr.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    bool st = pr.GetMethod?.IsStatic ?? false;
                    Console.WriteLine($"{(st ? "SPROP  " : "PROP   ")}{t.FullName}.{pr.Name} : {pr.PropertyType.Name}");
                }
            } catch {}
        }
    }
}
