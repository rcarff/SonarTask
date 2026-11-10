// Run with node Tools/LslBridge/test_browser.cjs. No browser/network required.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const sockets = [], callbacks = [], timers = [];
class Socket {
  constructor(url) { this.url = url; this.readyState = 0; this.bufferedAmount = 0; this.sent = []; sockets.push(this); }
  send(value) { assert.equal(this.readyState, 1); this.sent.push(value); }
  close() { this.readyState = 3; if (this.onclose) this.onclose(); }
  open() { this.readyState = 1; this.onopen(); }
  receive(message) { this.onmessage({data: JSON.stringify(message)}); }
}
const context = {
  window: {}, LibraryManager: {library: {}}, mergeInto: Object.assign,
  UTF8ToString: x => x, WebSocket: Socket, performance: {now: () => 10000},
  SendMessage: (...args) => callbacks.push(args),
  setTimeout: fn => { timers.push(fn); return timers.length; }, clearTimeout: () => {},
  setInterval: () => 1, clearInterval: () => {},
  HEAPF32: new Float32Array([1, 12, .1, .2]),
  ArrayBuffer, DataView, JSON
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../Assets/Plugins/WebGL/SonarLsl.jslib'), 'utf8'), context);
const lib = context.LibraryManager.library;
lib.SonarLslConnect('SONAR LSL', JSON.stringify({bins:256}), 16571);
const socket = sockets[0];
assert.equal(socket.url, 'ws://127.0.0.1:16571');
lib.SonarLslSend(JSON.stringify({kind:'marker', json:'begin', age:.25}));
assert.equal(socket.sent.length, 0);
socket.open();
assert.equal(JSON.parse(socket.sent[0]).kind, 'hello');
socket.receive({kind:'ready'});
assert.equal(JSON.parse(socket.sent[1]).json, 'begin');
assert.equal(JSON.parse(socket.sent[1]).timestamp, 9.75);
assert.equal(callbacks[0][1], 'OnLslStatus');
socket.bufferedAmount = 300000;
lib.SonarLslSend(JSON.stringify({kind:'marker', json:'one'}));
lib.SonarLslSend(JSON.stringify({kind:'marker', json:'two'}));
lib.SonarLslBinary(1, 0, 4);
assert.equal(socket.sent.length, 2);
socket.bufferedAmount = 0;
context.window.__sonarLsl.flush();
assert.deepEqual(socket.sent.slice(2).map(s => JSON.parse(s).json), ['one', 'two']);
lib.SonarLslBinary(1, 0, 4);
const frame = new DataView(socket.sent.at(-1));
assert.equal(frame.byteLength, 28);
assert.equal(frame.getUint32(0, true), 1);
assert.equal(frame.getFloat64(4, true), 10);
assert.equal(frame.getFloat32(12, true), 1);
assert.equal(frame.getFloat32(16, true), 12);
socket.receive({kind:'command', json:'{"cmd":"pause"}'});
assert.equal(callbacks.at(-1)[1], 'OnLslCommand');
socket.close();
assert.equal(callbacks.at(-1)[2], 'disconnected');
timers.at(-1)();
assert.equal(sockets.length, 2);
lib.SonarLslClose();
const count = callbacks.length;
socket.receive({kind:'ready'});
assert.equal(callbacks.length, count); // Stale callbacks from closed transports are ignored.
console.log('Browser LSL protocol, marker ordering, backpressure, binary framing and reconnect checks passed.');
