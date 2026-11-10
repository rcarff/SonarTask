import asyncio
import json
import logging
import math
from pathlib import Path
import struct
import sys
import time
import pylsl
from websockets.asyncio.server import serve
from websockets.asyncio.client import connect
from websockets.exceptions import InvalidStatus
from sonar_lsl_bridge import Bridge

logging.getLogger('websockets.server').setLevel(logging.CRITICAL)

async def main():
    bridge = Bridge(pylsl)
    config = dict(sourceId='sonar-bridge-integration', bins=256, views=2, stateRate=30,
                  waterfallRate=20, waterfalls=True, appVersion='test', platform='WebGL')
    inlets = []
    async with serve(bridge.handle, '127.0.0.1', 0, origins=['http://localhost:8765']) as server:
        port = server.sockets[0].getsockname()[1]
        url = f'ws://127.0.0.1:{port}'
        try:
            async with connect(url, origin='https://disallowed.example'):
                raise AssertionError('Disallowed Origin accepted')
        except InvalidStatus as exc:
            assert exc.response.status_code == 403
        async with connect(url, origin='http://localhost:8765') as socket:
            await socket.send(json.dumps(dict(kind='hello', config=config)))
            while json.loads(await socket.recv())['kind'] != 'ready':
                pass
            await socket.send(json.dumps(dict(kind='marker', timestamp=time.perf_counter(), json='{"eventType":"Run_Begin","seq":0}')))
            for suffix in ('Markers', 'State', 'BTH', 'LOFAR'):
                infos = await asyncio.to_thread(pylsl.resolve_byprop, 'source_id', config['sourceId'] + '-' + suffix, 1, 5)
                assert infos, suffix
                inlet = pylsl.StreamInlet(infos[0])
                await asyncio.to_thread(inlet.open_stream, 3)
                inlets.append(inlet)
            sample, stamp = await asyncio.to_thread(inlets[0].pull_sample, 3)
            assert json.loads(sample[0])['eventType'] == 'Run_Begin' and stamp > 0, 'Startup marker lost during discovery'
            await socket.send(json.dumps(dict(kind='marker', timestamp=time.perf_counter(), json='{"eventType":"integration","seq":1}')))
            sample, stamp = await asyncio.to_thread(inlets[0].pull_sample, 3)
            assert json.loads(sample[0])['seq'] == 1 and stamp > 0
            await socket.send(json.dumps(dict(kind='state', timestamp=time.perf_counter(), sample=[1,2,0,2,0,'NaN'])))
            sample, stamp = await asyncio.to_thread(inlets[1].pull_sample, 3)
            assert len(sample) == 6 and math.isnan(sample[-1])
            for stream, count, inlet in [(1,258,inlets[2]), (2,515,inlets[3])]:
                values = [float(i) / count for i in range(count)]
                frame = struct.pack('<Id', stream, time.perf_counter()) + struct.pack(f'<{count}f', *values)
                await socket.send(frame)
                sample, stamp = await asyncio.to_thread(inlet.pull_sample, 3)
                assert len(sample) == count and abs(sample[-1]-values[-1]) < 1e-6
            control = pylsl.StreamOutlet(pylsl.StreamInfo('SonarTask-Control','SonarControl',1,0,'string','bridge-test-control'))
            assert await asyncio.to_thread(control.wait_for_consumers, 8), 'No control inlet'
            control.push_sample(['{"cmd":"pause","id":"bridge-test"}'])
            async def read_command():
                while True:
                    msg = json.loads(await socket.recv())
                    if msg['kind'] == 'command':
                        return json.loads(msg['json'])
                    if msg['kind'] == 'ping':
                        await socket.send(json.dumps(dict(kind='pong', sent=msg['sent'], timestamp=time.perf_counter())))
            command = await asyncio.wait_for(read_command(), 5)
            assert command == dict(cmd='pause', id='bridge-test')
    for inlet in inlets:
        inlet.close_stream()
    bridge.streams.closed.set()
    await asyncio.to_thread(bridge.streams.thread.join, 3)
    print('Real WebSocket/pylsl integration passed: Origin rejection, markers, NaN state, BTH/LOFAR binary samples and inbound control.')

if __name__ == '__main__':
    asyncio.run(main())
