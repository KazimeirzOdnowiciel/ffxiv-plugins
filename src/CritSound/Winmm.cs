using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CritSound;

/// <summary>
/// Thin wrapper over WinMM's PlaySound. Playing from memory lets us apply a volume
/// scale without touching the system mixer, and keeps the plugin free of audio libraries.
/// </summary>
internal static class Winmm
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;
    private const uint SndFilename = 0x00020000;

    /// <summary>How long a pinned buffer may stay alive after playback started.</summary>
    private const long BufferLifetimeMs = 5000;

    // PlaySound(SND_ASYNC) reads from our buffer for the duration of the sound, so it has to stay
    // pinned until playback is safely over. All calls happen on the framework thread.
    private static readonly List<(GCHandle Handle, long StartedAt)> Pending = new();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundFile(string sound, IntPtr module, uint flags);

    public static bool PlayMemory(byte[] wav)
    {
        if (wav.Length == 0)
            return false;

        var now = Environment.TickCount64;
        Purge(now);

        var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
        try
        {
            if (!PlaySound(handle.AddrOfPinnedObject(), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault))
            {
                handle.Free();
                return false;
            }
        }
        catch
        {
            if (handle.IsAllocated)
                handle.Free();
            throw;
        }

        Pending.Add((handle, now));
        return true;
    }

    public static bool PlayFile(string path)
    {
        Purge(Environment.TickCount64);
        return PlaySoundFile(path, IntPtr.Zero, SndFilename | SndAsync | SndNoDefault);
    }

    private static void Purge(long now)
    {
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (now - Pending[i].StartedAt < BufferLifetimeMs)
                continue;

            if (Pending[i].Handle.IsAllocated)
                Pending[i].Handle.Free();

            Pending.RemoveAt(i);
        }
    }

    public static void ReleaseAll()
    {
        foreach (var entry in Pending)
        {
            if (entry.Handle.IsAllocated)
                entry.Handle.Free();
        }

        Pending.Clear();
    }
}
