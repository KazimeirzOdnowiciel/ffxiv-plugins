using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Mountbook.Previews.Cjk;

/// <summary>
/// Loads the ImGui bindings this tool draws with, from the Dalamud installation.
/// </summary>
/// <remarks>
/// The bindings are a reference, not a package: <c>Dalamud.Bindings.ImGui.dll</c>, its proxy generator and
/// the native <c>cimgui.dll</c> all come with Dalamud, and a plugin never ships them. The pinned preview
/// tool solves the same problem with a <c>--dalamud</c> flag; this does it from <c>DALAMUD_HOME</c>, the
/// folder Dalamud's own tooling already uses, so the command line stays short.
/// </remarks>
internal static class Dalamud
{
    /// <summary>Where a Dalamud install keeps the assemblies a plugin compiles against.</summary>
    private static readonly string Fallback = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XIVLauncher",
        "addon",
        "Hooks",
        "dev");

    /// <summary>
    /// Makes the bindings resolvable from the Dalamud folder. The native library is loaded first: Windows
    /// binds a later <c>DllImport</c> to the module already in the process, which is what stops
    /// <c>cimgui</c> being looked for beside this executable.
    /// </summary>
    /// <returns>The folder it resolved from.</returns>
    /// <exception cref="InvalidOperationException">No folder holds the bindings.</exception>
    public static string Load()
    {
        string folder = Environment.GetEnvironmentVariable("DALAMUD_HOME") is { Length: > 0 } configured
            ? configured
            : Fallback;

        string bindings = Path.Combine(folder, "Dalamud.Bindings.ImGui.dll");

        if (!File.Exists(bindings))
        {
            throw new InvalidOperationException(
                $"Dalamud.Bindings.ImGui.dll is not in '{folder}'; set DALAMUD_HOME to the folder that has it.");
        }

        string native = Path.Combine(folder, "cimgui.dll");

        if (File.Exists(native))
        {
            _ = NativeLibrary.Load(native);
        }

        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(folder, name.Name + ".dll");
            return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
        };

        _ = AssemblyLoadContext.Default.LoadFromAssemblyPath(bindings);

        return folder;
    }
}
