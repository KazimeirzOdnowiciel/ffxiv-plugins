"""Generates the three bundled catch sounds for CritSound.

Run from the repo root:  python tools/make_sounds.py
"""

from __future__ import annotations

import math
import os
import struct
import wave

import numpy as np

SAMPLE_RATE = 44100
OUT_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "Sounds")


def envelope(n: int, decay_tau: float, attack_s: float = 0.003) -> np.ndarray:
    t = np.arange(n) / SAMPLE_RATE
    env = np.exp(-t / decay_tau)
    attack = int(attack_s * SAMPLE_RATE)
    if attack > 0:
        env[:attack] *= np.linspace(0.0, 1.0, attack)
    return env


def tone(freq: float, dur: float, decay_tau: float, detune: float = 0.0) -> np.ndarray:
    n = int(dur * SAMPLE_RATE)
    t = np.arange(n) / SAMPLE_RATE
    sig = np.sin(2 * math.pi * freq * t)
    if detune:
        sig += 0.5 * np.sin(2 * math.pi * (freq + detune) * t)
    return sig * envelope(n, decay_tau)


def transient(dur: float, decay_tau: float, seed: int) -> np.ndarray:
    n = int(dur * SAMPLE_RATE)
    rng = np.random.default_rng(seed)
    noise = rng.normal(0.0, 1.0, n)
    # One-pole low-pass so the click is a thud, not a hiss.
    out = np.zeros(n)
    prev = 0.0
    alpha = 0.35
    for i in range(n):
        prev = prev + alpha * (noise[i] - prev)
        out[i] = prev
    return out * envelope(n, decay_tau, attack_s=0.0005)


def mix(*parts: np.ndarray) -> np.ndarray:
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for part in parts:
        out[: len(part)] += part
    return out


def finish(sig: np.ndarray, peak: float = 0.7) -> np.ndarray:
    top = float(np.max(np.abs(sig)))
    if top > 0:
        sig = sig / top * peak
    tail = min(int(0.006 * SAMPLE_RATE), len(sig))
    sig[-tail:] *= np.linspace(1.0, 0.0, tail)
    return sig


def write(name: str, sig: np.ndarray) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    pcm = np.clip(sig * 32767.0, -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as handle:
        handle.setnchannels(1)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        handle.writeframes(pcm.tobytes())
    print(f"{name:22s} {len(sig) / SAMPLE_RATE:5.3f}s  peak={float(np.max(np.abs(sig))):.3f}")


def crit() -> np.ndarray:
    sig = mix(
        tone(2637.0, 0.24, 0.045),          # E7
        tone(3951.0, 0.16, 0.030) * 0.55,   # B7
        tone(1318.5, 0.20, 0.060) * 0.35,   # E6 body
        transient(0.03, 0.004, seed=1) * 0.35,
    )
    return finish(sig)


def direct_hit() -> np.ndarray:
    sig = mix(
        tone(880.0, 0.16, 0.038),
        tone(1320.0, 0.12, 0.026) * 0.5,
        transient(0.04, 0.006, seed=2) * 0.55,
    )
    return finish(sig, peak=0.6)


def crit_direct_hit() -> np.ndarray:
    body = mix(
        tone(1568.0, 0.42, 0.120),          # G6
        tone(2349.0, 0.36, 0.090) * 0.6,    # D7
        tone(3136.0, 0.30, 0.070) * 0.4,    # G7
        tone(784.0, 0.40, 0.150) * 0.35,    # G5 body
        transient(0.03, 0.004, seed=3) * 0.30,
    )
    # Slow shimmer so the tail feels like a "chime" instead of a flat beep.
    t = np.arange(len(body)) / SAMPLE_RATE
    shimmer = 1.0 + 0.10 * np.sin(2 * math.pi * 7.0 * t) * np.clip((t - 0.05) / 0.2, 0.0, 1.0)
    return finish(body * shimmer, peak=0.8)


if __name__ == "__main__":
    write("crit.wav", crit())
    write("direct-hit.wav", direct_hit())
    write("crit-direct-hit.wav", crit_direct_hit())
