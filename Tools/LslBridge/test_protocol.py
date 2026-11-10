"""Protocol and bridge lifecycle tests; native LSL objects are replaced by fakes."""
import asyncio
import json
import math
import struct
import unittest
from sonar_lsl_bridge import Bridge, decode_waterfall, validate_config


def config():
    return dict(sourceId="test-machine", bins=256, views=2, stateRate=30,
                waterfallRate=20, waterfalls=True, appVersion="test", platform="WebGL")


class ProtocolTests(unittest.TestCase):
    def test_bth_and_lofar_channels_and_nan_survive_wire_format(self):
        c = config()
        for stream, count in ((1, 258), (2, 515)):
            values = [float(i) for i in range(count)]
            values[-1] = float("nan")
            data = struct.pack("<Id", stream, 123.25) + struct.pack(f"<{count}f", *values)
            output, timestamp, sample = decode_waterfall(data, c)
            self.assertEqual(output, stream + 1)
            self.assertEqual(timestamp, 123.25)
            self.assertEqual(sample[:-1], tuple(values[:-1]))
            self.assertTrue(math.isnan(sample[-1]))

    def test_truncated_unknown_and_mismatched_frames_are_rejected(self):
        for data in (b"", struct.pack("<Id", 3, 1), struct.pack("<Id", 1, 1),
                     struct.pack("<Id", 1, float("nan")) + bytes(258 * 4)):
            with self.assertRaises(ValueError):
                decode_waterfall(data, config())

    def test_invalid_allocation_and_rates_are_rejected(self):
        for field, value in (("bins", 128), ("views", 0), ("views", 1000000),
                             ("stateRate", float("nan")), ("waterfallRate", -1), ("sourceId", "")):
            c = config()
            c[field] = value
            with self.assertRaises(ValueError):
                validate_config(c)


class FakeInfo:
    def __init__(self, *args):
        self.args = args
    def desc(self):
        return self
    def append_child_value(self, *args):
        pass


class FakeOutlet:
    def __init__(self, info, **kwargs):
        self.info = info
        self.samples = []
    def push_sample(self, sample, timestamp):
        self.samples.append((sample, timestamp))
    def have_consumers(self):
        return True


class FakeLsl:
    StreamInfo = FakeInfo
    cf_string = 3
    def __init__(self):
        self.outlets = []
    def StreamOutlet(self, info, **kwargs):
        result = FakeOutlet(info, **kwargs)
        self.outlets.append(result)
        return result
    def resolve_byprop(self, *args, **kwargs):
        return []
    def local_clock(self):
        return 100.0


class FakeSocket:
    def __init__(self, messages):
        self.messages = iter(messages)
        self.sent = []
        self.closed = None
    async def recv(self):
        return next(self.messages)
    def __aiter__(self):
        return self
    async def __anext__(self):
        await asyncio.sleep(.01)
        try:
            return next(self.messages)
        except StopIteration:
            raise StopAsyncIteration
    async def send(self, value):
        self.sent.append(value)
    async def close(self, code, reason):
        self.closed = code


class BridgeTests(unittest.IsolatedAsyncioTestCase):
    async def test_marker_order_fixed_channels_nan_state_and_reconnect(self):
        lsl = FakeLsl()
        bridge = Bridge(lsl)
        hello = json.dumps(dict(kind="hello", config=config()))
        markers = [json.dumps(dict(kind="marker", timestamp=1, json=json.dumps(dict(seq=i)))) for i in range(10)]
        socket = FakeSocket([hello, *markers,
            json.dumps(dict(kind="state", timestamp=1, sample=[1, 2, 0, 2, 0, "NaN"]))])
        try:
            await bridge.handle(socket)
            await asyncio.sleep(.02)
            self.assertEqual(len(lsl.outlets), 4)
            self.assertEqual([o.info.args[2] for o in lsl.outlets], [1, 6, 258, 515])
            self.assertEqual([json.loads(s[0][0])["seq"] for s in lsl.outlets[0].samples], list(range(10)))
            self.assertTrue(math.isnan(lsl.outlets[1].samples[0][0][-1]))
            await bridge.handle(FakeSocket([hello]))
            self.assertEqual(len(lsl.outlets), 4)  # Same stream config reuses outlets.
            self.assertFalse(bridge.busy)
        finally:
            bridge.streams.closed.set()
            await asyncio.to_thread(bridge.streams.thread.join, 2)

    async def test_second_page_and_missing_hello_are_rejected(self):
        bridge = Bridge(FakeLsl())
        bridge.busy = True
        second = FakeSocket([])
        await bridge.handle(second)
        self.assertEqual(second.closed, 1013)
        bridge.busy = False
        invalid = FakeSocket(['{"kind":"state"}'])
        await bridge.handle(invalid)
        self.assertEqual(invalid.closed, 1008)


if __name__ == "__main__":
    unittest.main()
