mergeInto(LibraryManager.library, {
  SonarLslConnect: function(targetPtr, helloPtr, port) {
    if (window.__sonarLsl) window.__sonarLsl.close();
    var target = UTF8ToString(targetPtr), hello = JSON.parse(UTF8ToString(helloPtr));
    var state = { socket: null, stopped: false, timer: null, delay: 500, pending: [] };
    var callback = function(method, value) { SendMessage(target, method, value); };
    state.close = function() {
      state.stopped = true; clearTimeout(state.timer);
      if (state.socket) state.socket.close();
    };
    state.flush = function() {
      if (!state.socket || state.socket.readyState !== 1) return;
      while (state.pending.length && state.socket.bufferedAmount < 262144)
        state.socket.send(state.pending.shift());
    };
    var connect = function() {
      if (state.stopped) return;
      try {
        var socket = state.socket = new WebSocket('ws://127.0.0.1:' + port);
        socket.onopen = function() {
          state.delay = 500;
          socket.send(JSON.stringify({ kind: 'hello', config: hello, timestamp: performance.now() / 1000 }));
        };
        socket.onmessage = function(event) {
          if (state.stopped) return;
          try {
            var message = JSON.parse(event.data);
            if (message.kind === 'ready') { callback('OnLslStatus', 'connected'); state.flush(); }
            else if (message.kind === 'ping')
              socket.send(JSON.stringify({ kind: 'pong', timestamp: performance.now() / 1000, sent: message.sent }));
            else if (message.kind === 'command') callback('OnLslCommand', message.json);
          } catch (e) {}
        };
        socket.onclose = function() {
          if (!state.stopped) callback('OnLslStatus', 'disconnected');
          // Disconnected markers are not replayed with misleading new timestamps.
          state.pending = [];
          if (!state.stopped) {
            state.timer = setTimeout(connect, state.delay);
            state.delay = Math.min(10000, state.delay * 2);
          }
        };
        socket.onerror = function() { socket.close(); };
      } catch (e) { if (!state.stopped) state.timer = setTimeout(connect, 10000); }
    };
    window.__sonarLsl = state;
    state.pump = setInterval(function() { if (state.stopped) clearInterval(state.pump); else state.flush(); }, 10);
    connect();
  },
  SonarLslSend: function(jsonPtr) {
    var state = window.__sonarLsl;
    if (!state || state.stopped || !state.socket) return;
    var message = JSON.parse(UTF8ToString(jsonPtr));
    message.timestamp = performance.now() / 1000 - (message.age || 0);
    var json = JSON.stringify(message);
    if (message.kind === 'marker') { state.pending.push(json); state.flush(); }
    else if (state.socket.readyState === 1 && state.socket.bufferedAmount < 262144 && state.pending.length === 0) state.socket.send(json);
  },
  SonarLslBinary: function(stream, values, count) {
    var state = window.__sonarLsl;
    if (!state || !state.socket || state.socket.readyState !== 1) return;
    state.flush();
    // Drop live waterfall frames under backpressure rather than adding latency.
    if (state.pending.length || state.socket.bufferedAmount > 262144) return;
    // Little endian: uint32 stream id, float64 performance seconds, float32 channels.
    var buffer = new ArrayBuffer(12 + count * 4), view = new DataView(buffer);
    view.setUint32(0, stream, true); view.setFloat64(4, performance.now() / 1000, true);
    for (var i = 0; i < count; i++) view.setFloat32(12 + i * 4, HEAPF32[(values >> 2) + i], true);
    state.socket.send(buffer);
  },
  SonarLslClose: function() { if (window.__sonarLsl) window.__sonarLsl.close(); }
});
