"""Loopback WebSocket to LSL bridge. See README.md for installation and timing."""
import argparse
import asyncio
from collections import deque
import json
import math
import queue
import re
import struct
import threading
import time


def validate_config(config):
    """Bound allocations from an allowed browser before creating native outlets."""
    bins, views = config.get("bins"), config.get("views")
    rate = config.get("stateRate")
    scan = config.get("waterfallRate")
    if bins not in (256, 512, 1024) or not isinstance(views, int) or not 1 <= views <= 32:
        raise ValueError("Invalid waterfall dimensions")
    if not isinstance(rate, (int, float)) or not math.isfinite(rate) or not 1 <= rate <= 250:
        raise ValueError("Invalid state rate")
    if not isinstance(scan, (int, float)) or not math.isfinite(scan) or scan < 0:
        raise ValueError("Invalid waterfall rate")
    if not isinstance(config.get("sourceId"), str) or not 1 <= len(config["sourceId"]) <= 128:
        raise ValueError("Invalid source ID")
    return config


def decode_waterfall(data, config):
    if len(data) < 12:
        raise ValueError("Truncated waterfall header")
    stream, timestamp = struct.unpack_from("<Id", data)
    count = {1: 2 + config["bins"], 2: 3 + config["bins"] * config["views"]}.get(stream)
    if count is None or len(data) != 12 + count * 4 or not math.isfinite(timestamp):
        raise ValueError("Invalid waterfall frame")
    return stream + 1, timestamp, struct.unpack_from(f"<{count}f", data, 12)


class Streams:
    """One worker owns native LSL objects; only waterfall/state samples may be dropped."""
    def __init__(self, config, pylsl):
        self.config = config
        self.lsl = pylsl
        self.markers = queue.SimpleQueue()
        self.frames = deque(maxlen=8)
        self.state = deque(maxlen=2)
        self.closed = threading.Event()
        self.ready = threading.Event()
        self.error = None
        self.receiver = None
        self.thread = threading.Thread(target=self.work, daemon=True)
        self.thread.start()

    def outlet(self, name, kind, count, rate, fmt):
        info = self.lsl.StreamInfo("SonarTask-" + name, kind, count, rate, fmt,
                                   self.config["sourceId"] + "-" + name)
        for key, value in (("app_version", self.config.get("appVersion", "")),
                           ("platform", self.config.get("platform", "WebGL")), ("schema_version", "1")):
            info.desc().append_child_value(key, str(value))
        return self.lsl.StreamOutlet(info, max_buffered=10)

    def work(self):
        inlet = None
        try:
            c = self.config
            outlets = [self.outlet("Markers", "Markers", 1, 0, "string"),
                       self.outlet("State", "SonarState", 6, c["stateRate"], "float32")]
            if c["waterfalls"]:
                outlets += [self.outlet("BTH", "SonarWaterfall", 2 + c["bins"], c["waterfallRate"], "float32"),
                            self.outlet("LOFAR", "SonarWaterfall", 3 + c["bins"] * c["views"], c["waterfallRate"], "float32")]
            self.ready.set()
            next_resolve = 0
            while not self.closed.is_set() or not self.markers.empty() and outlets[0].have_consumers():
                for _ in range(256):
                    if not outlets[0].have_consumers():
                        break
                    try:
                        sample, timestamp = self.markers.get_nowait()
                    except queue.Empty:
                        break
                    outlets[0].push_sample([sample], timestamp)
                for frames in (self.state, self.frames):
                    while frames:
                        stream, timestamp, sample = frames.popleft()
                        outlets[stream].push_sample(sample, timestamp)
                if inlet is None and time.monotonic() >= next_resolve and not self.closed.is_set():
                    found = self.lsl.resolve_byprop("name", "SonarTask-Control", timeout=.05)
                    for info in found:
                        if info.channel_count() == 1 and info.channel_format() == self.lsl.cf_string:
                            inlet = self.lsl.StreamInlet(info, max_buflen=1, recover=False)
                            break
                    next_resolve = time.monotonic() + 2
                if inlet:
                    try:
                        for _ in range(64):
                            sample, _ = inlet.pull_sample(timeout=0)
                            if sample is None:
                                break
                            if self.receiver and len(sample[0]) <= 8192:
                                self.receiver(sample[0])
                    except Exception:
                        inlet.close_stream()
                        inlet = None
                self.closed.wait(.002)
        except Exception as exc:
            self.error = exc
            self.ready.set()
        finally:
            if inlet:
                inlet.close_stream()


class Bridge:
    def __init__(self, pylsl):
        self.lsl = pylsl
        self.streams = None
        self.busy = False

    async def handle(self, socket):
        if self.busy:
            await socket.close(1013, "Another SonarTask page is connected")
            return
        self.busy = True
        outgoing = asyncio.Queue(maxsize=256)
        loop = asyncio.get_running_loop()
        sender = ping = None
        try:
            hello = json.loads(await asyncio.wait_for(socket.recv(), 5))
            if hello.get("kind") != "hello":
                raise ValueError("Expected hello")
            config = validate_config(hello["config"])
            if self.streams is None or self.streams.config != config:
                if self.streams:
                    self.streams.closed.set()
                    await asyncio.to_thread(self.streams.thread.join, 3)
                self.streams = Streams(config, self.lsl)
            streams = self.streams
            ready = await asyncio.to_thread(streams.ready.wait, 5)
            if not ready or streams.error:
                raise ValueError("Native LSL failed to initialize: " + str(streams.error))
            # Receipt time is the fallback until a ping estimates clock offset.
            offset = None
            best_rtt = float("inf")

            def enqueue(command):
                if not outgoing.full():
                    outgoing.put_nowait(json.dumps({"kind": "command", "json": command}))

            streams.receiver = lambda cmd: loop.call_soon_threadsafe(enqueue, cmd)

            async def send_commands():
                while True:
                    await socket.send(await outgoing.get())

            async def ping_clock():
                while True:
                    await socket.send(json.dumps({"kind": "ping", "sent": self.lsl.local_clock()}))
                    await asyncio.sleep(2)

            await socket.send('{"kind":"ready"}')
            sender = asyncio.create_task(send_commands())
            ping = asyncio.create_task(ping_clock())
            async for data in socket:
                received = self.lsl.local_clock()
                if streams.error:
                    raise ValueError("LSL publisher failed")
                if isinstance(data, bytes):
                    stream, timestamp, sample = decode_waterfall(data, config)
                    if config["waterfalls"]:
                        streams.frames.append((stream, received if offset is None else min(received, timestamp + offset), sample))
                    continue
                message = json.loads(data)
                kind = message.get("kind")
                stamp = float(message.get("timestamp", 0))
                if not math.isfinite(stamp):
                    raise ValueError("Non-finite timestamp")
                timestamp = received if offset is None else min(received, stamp + offset)
                if kind == "pong":
                    sent = float(message["sent"])
                    rtt = received - sent
                    if 0 <= rtt < best_rtt:
                        best_rtt = rtt
                        offset = (sent + received) / 2 - stamp
                elif kind == "marker":
                    if not isinstance(message.get("json"), str):
                        raise ValueError("Marker must be JSON text")
                    streams.markers.put((message["json"], timestamp))
                elif kind == "state":
                    sample = message["sample"]
                    if len(sample) != 6:
                        raise ValueError("State requires six channels")
                    # JSON null or Newtonsoft's string NaN denotes no BTH selection.
                    sample = [float("nan") if x is None or x == "NaN" else float(x) for x in sample]
                    streams.state.append((1, timestamp, sample))
        except Exception as exc:
            print("Bridge connection ended:", exc)
            await socket.close(1008, "Invalid session or disconnected publisher")
        finally:
            if self.streams:
                self.streams.receiver = None
            for task in (sender, ping):
                if task:
                    task.cancel()
            await asyncio.gather(*(t for t in (sender, ping) if t), return_exceptions=True)
            self.busy = False


async def main(args):
    import pylsl
    from websockets.asyncio.server import serve
    bridge = Bridge(pylsl)
    origins = [re.compile(r"https?://(?:localhost|127\.0\.0\.1|\[::1\])(?::\d+)?"), *args.origin]
    async with serve(bridge.handle, "127.0.0.1", args.port, origins=origins,
                     max_size=2**20, max_queue=8, compression=None):
        print(f"SonarTask LSL bridge listening on 127.0.0.1:{args.port}")
        print("Allowed study origins:", args.origin or "(localhost only)")
        try:
            await asyncio.Future()
        finally:
            if bridge.streams:
                bridge.streams.closed.set()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=16571)
    parser.add_argument("--origin", action="append", default=[], help="Exact allowed study origin, e.g. https://study.example.edu")
    asyncio.run(main(parser.parse_args()))
