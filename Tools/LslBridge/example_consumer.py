"""Print markers and continuous state from either platform."""
import time
from pylsl import StreamInlet, resolve_byprop

inlets = {}
try:
    while True:
        for name in ("SonarTask-Markers", "SonarTask-State"):
            if name not in inlets:
                found = resolve_byprop("name", name, timeout=.1)
                if found:
                    inlets[name] = StreamInlet(found[0])
            if name in inlets:
                for _ in range(100):
                    sample, stamp = inlets[name].pull_sample(timeout=0)
                    if sample is None:
                        break
                    print(name, f"{stamp:.6f}", sample)
        time.sleep(.01)
except KeyboardInterrupt:
    pass
