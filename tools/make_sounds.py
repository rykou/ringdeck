"""Builds RingDeck.App/Assets/open.wav and select.wav: short synthesized clicks, like a phone keyboard tap.

Made from sine waves and noise here, so there is no third-party audio to license. Run: python3 tools/make_sounds.py
"""
import math
import random
import struct
import wave
from pathlib import Path

RATE = 44100
ASSETS = Path(__file__).resolve().parent.parent / "RingDeck.App" / "Assets"


def click(partials, noise, noise_decay, length=0.045, seed=1):
    """partials: (frequency Hz, level, decay seconds). The noise is high-passed so it reads as a crisp tick."""
    rng = random.Random(seed)
    samples, previous = [], 0.0
    for i in range(int(RATE * length)):
        t = i / RATE
        value = sum(level * math.exp(-t / decay) * math.sin(2 * math.pi * freq * t) for freq, level, decay in partials)
        white = rng.uniform(-1, 1)
        value += noise * math.exp(-t / noise_decay) * (white - previous)
        previous = white
        value *= min(1.0, t / 0.0003)                       # 0.3 ms attack, no pop at the start
        value *= min(1.0, (length - t) / 0.005)             # 5 ms fade, no pop at the end
        samples.append(value)
    peak = max(abs(s) for s in samples)
    return [s / peak * 0.7 for s in samples]                # about -3 dB


def save(name, samples):
    with wave.open(str(ASSETS / name), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(b"".join(struct.pack("<h", int(s * 32767)) for s in samples))


# Opening a ring: a soft, slightly lower tock.
save("open.wav", click([(900, 0.6, 0.006), (2100, 0.35, 0.0025)], noise=0.25, noise_decay=0.0008))
# Choosing a slice: the crisp tick.
save("select.wav", click([(1800, 0.5, 0.003), (4200, 0.35, 0.0012)], noise=0.45, noise_decay=0.0006, seed=2))
