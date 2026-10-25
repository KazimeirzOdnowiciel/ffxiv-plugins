using System;
using System.Threading;
using SlotYield.Core;
using SlotYield.Market;

namespace SlotYield;

/// <summary>
/// Mutable snapshot of the scanner, written from the background scan and read by the UI.
/// Individual field writes are atomic; the UI tolerates a frame of skew.
/// </summary>
public sealed class ScanState
{
    public volatile bool IsScanning;

    public volatile string Phase = string.Empty;

    public volatile string Detail = string.Empty;

    public volatile int Done;

    public volatile int Total;

    public volatile string Error = string.Empty;

    public ScanDiff? Diff;

    public ScanResult? Result;

    public CancellationTokenSource? Cancellation { get; set; }

    public float Fraction => this.Total <= 0 ? 0f : Math.Clamp(this.Done / (float)this.Total, 0f, 1f);
}
