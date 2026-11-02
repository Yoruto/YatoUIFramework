"""Regenerate the sample's original synthesized WAV assets (Python standard library only)."""
from pathlib import Path
import math
import random
import struct
import wave

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "Assets/GameRes/Audio/Survivor"
SAMPLE_RATE = 22050


def write(name, duration, sample):
    data = bytearray()
    for index in range(int(duration * SAMPLE_RATE)):
        value = max(-1, min(1, sample(index / SAMPLE_RATE)))
        data.extend(struct.pack("<h", int(value * 32767)))
    with wave.open(str(OUTPUT / (name + ".wav")), "wb") as clip:
        clip.setnchannels(1)
        clip.setsampwidth(2)
        clip.setframerate(SAMPLE_RATE)
        clip.writeframes(data)


def music(time):
    notes = [110, 87.307, 73.416, 82.407]
    bass = notes[int(time // 4) % 4]
    beat = time % 0.5
    value = 0.12 * math.sin(2 * math.pi * bass * time) * math.exp(-beat * 5)
    for ratio in [2, 2.378414, 2.996614]:
        value += 0.025 * math.sin(2 * math.pi * bass * ratio * time) * math.sin(math.pi * (time % 4) / 4) ** 2
    note = [440, 523.251, 659.255, 587.33, 523.251, 440, 392, 329.628][int(time * 2) % 8]
    return value + 0.025 * math.sin(2 * math.pi * note * time) * math.exp(-beat * 12)


if __name__ == "__main__":
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write("watch", 16, music)
    noise = random.Random(209)
    write("hit", 0.14, lambda t: (noise.random() * 2 - 1) * 0.22 * math.exp(-t * 30)
          + 0.15 * math.sin(2 * math.pi * (160 * t - 250 * t * t)) * math.exp(-t * 25))
    write("pickup", 0.15, lambda t: 0.15 * math.sin(2 * math.pi * (900 * t + 1800 * t * t)) * math.exp(-t * 30))
    write("upgrade", 0.6, lambda t: 0.15 * math.sin(2 * math.pi * [523.251, 659.255, 783.991, 1046.502][min(3, int(t / 0.15))] * t)
          * math.sin(math.pi * (t % 0.15) / 0.15))
    print("Generated watch.wav, hit.wav, pickup.wav, upgrade.wav")
