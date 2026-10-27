using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CrashTrace;

/// <summary>
/// Diagnostic plugin: records every first-chance managed exception with its live stack.
/// The last entry in dalamud.log before a crash names the code that threw, including
/// the plugin file and line, which the crash dump itself cannot recover.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const int MaxEntries = 5000;

    private int count;
    private bool reentrant;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();
        AppDomain.CurrentDomain.FirstChanceException += this.OnFirstChance;
        Service.Log.Information("[CrashTrace] armed - logging first-chance exceptions");
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.FirstChanceException -= this.OnFirstChance;
        Service.Log.Information("[CrashTrace] disarmed");
    }

    private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (this.reentrant || this.count >= MaxEntries)
            return;

        this.reentrant = true;
        try
        {
            this.count++;
            var exception = args.Exception;
            var trace = new StackTrace(1, true).ToString();
            Service.Log.Warning(
                "[CrashTrace] #{Count} {Type}: {Message}\n{Trace}",
                this.count,
                exception.GetType().FullName,
                exception.Message,
                trace);
        }
        catch
        {
            // the tracer must never throw
        }
        finally
        {
            this.reentrant = false;
        }
    }
}

internal sealed class Service
{
    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;
}
