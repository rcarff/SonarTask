"""Publish commands to either a native app or the web bridge."""
import argparse
import json
import time
import uuid
from pylsl import StreamInfo, StreamOutlet

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("cmd", choices=["start", "pause", "resume", "marker", "alert"])
parser.add_argument("text", nargs="?", default="External marker")
parser.add_argument("bearing", nargs="?", type=float, default=135)
parser.add_argument("--duration", type=float, default=10)
args = parser.parse_args()
outlet = StreamOutlet(StreamInfo("SonarTask-Control", "SonarControl", 1, 0, "string", "sonartask-controller"))
print("Waiting up to 15 seconds for the app/bridge control inlet...")
if not outlet.wait_for_consumers(15):
    raise SystemExit("No control consumer. Enable LSL and load an experiment first.")
payload = dict(cmd=args.cmd, text=args.text, bearing=args.bearing, durationSec=args.duration, id=str(uuid.uuid4()))
outlet.push_sample([json.dumps(payload)])
time.sleep(.5)
print(payload)
