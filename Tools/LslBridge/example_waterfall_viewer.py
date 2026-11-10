"""Rolling BTH/LOFAR images; applies phase axes from marker stream."""
import json
from collections import deque
import matplotlib.pyplot as plt
import numpy as np
from pylsl import StreamInlet, resolve_byprop

names = ("SonarTask-BTH", "SonarTask-LOFAR", "SonarTask-Markers")
inlets = {}
for name in names:
    print("Waiting for", name)
    found = resolve_byprop("name", name, timeout=60)
    if not found:
        raise SystemExit("Stream not found: " + name)
    inlets[name] = StreamInlet(found[0])
bins = inlets[names[0]].info().channel_count() - 2
views = (inlets[names[1]].info().channel_count() - 3) // bins
fig, axes = plt.subplots(views + 1, 1, squeeze=False)
axes = axes[:, 0]
history = [deque(maxlen=300) for _ in axes]
images = [ax.imshow(np.zeros((300, bins)), vmin=0, vmax=1, cmap="Greens", aspect="auto") for ax in axes]
axes[0].set_title("BTH (bearing degrees)")
for i, ax in enumerate(axes[1:]):
    ax.set_title(f"LOFAR {i + 1} (Hz)")
plt.ion()
plt.show()
phase = None
while plt.fignum_exists(fig.number):
    for name in names:
        for _ in range(256):
            sample, _ = inlets[name].pull_sample(timeout=0)
            if sample is None:
                break
            if name == names[2]:
                marker = json.loads(sample[0])
                if marker["eventType"] == "Waterfall_Axes":
                    axes[0].set_xlabel(f'{marker["bth"]["DegStart"]} → {marker["bth"]["DegEnd"]} degrees (wrapped)')
                    for i, ax in enumerate(axes[1:]):
                        active = i < len(marker["lofar"])
                        ax.set_visible(active)
                        if active:
                            v = marker["lofar"][i]
                            ax.set_xlabel(f'{v["HzStart"]} → {v["HzEnd"]} Hz')
                continue
            if phase != sample[0]:
                phase = sample[0]
                for rows in history:
                    rows.clear()
            rows = [sample[2:]] if name == names[0] else np.asarray(sample[3:]).reshape(views, bins)
            start = 0 if name == names[0] else 1
            for i, row in enumerate(rows):
                history[start + i].appendleft(row)
    for image, rows in zip(images, history):
        if rows:
            image.set_data(np.asarray(rows))
    plt.pause(.03)
