mergeInto(LibraryManager.library, {
  SonarInstallAudioUnlock: function() {
    if (typeof window === 'undefined' || window.__sonarAudioUnlockInstalled) return;
    window.__sonarAudioUnlockInstalled = true;

    var getContext = function() {
      try {
        if (typeof WEBAudio !== 'undefined' && WEBAudio && WEBAudio.audioContext) return WEBAudio.audioContext;
        if (typeof Module !== 'undefined' && Module) {
          if (Module.audioContext) return Module.audioContext;
          if (Module['audioContext']) return Module['audioContext'];
        }
      } catch (e) {}
      return null;
    };

    var cleanup = function() {
      document.removeEventListener('pointerdown', unlock, true);
      document.removeEventListener('touchstart', unlock, true);
      document.removeEventListener('keydown', unlock, true);
      window.removeEventListener('mousedown', unlock, true);
    };

    var unlock = function() {
      try {
        var ctx = getContext();
        if (!ctx) return;
        if (ctx.state === 'running') { cleanup(); return; }
        if (ctx.state === 'suspended') {
          var result = ctx.resume();
          if (result && result.then) {
            result.then(function() { if (ctx.state === 'running') cleanup(); }).catch(function() {});
          } else if (ctx.state === 'running') cleanup();
        }
      } catch (e) {}
    };

    document.addEventListener('pointerdown', unlock, true);
    document.addEventListener('touchstart', unlock, true);
    document.addEventListener('keydown', unlock, true);
    window.addEventListener('mousedown', unlock, true);
  },
  SonarDownloadText: function(namePtr, mimePtr, dataPtr) {
    var name=UTF8ToString(namePtr), mime=UTF8ToString(mimePtr), data=UTF8ToString(dataPtr);
    var a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([data],{type:mime})); a.download=name; a.click(); setTimeout(function(){URL.revokeObjectURL(a.href);},1000);
  },
  SonarOpenUrl: function(urlPtr) { window.open(UTF8ToString(urlPtr),'_blank'); },
  SonarPickZip: function(goPtr, methodPtr) {
    var go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
    var i = document.createElement('input');
    i.type = 'file';
    i.accept = '.zip,application/zip';
    i.style.display = 'none';
    document.body.appendChild(i);

    var send = function(message) {
      try { SendMessage(go, method, message); } catch (e) { console.error('SONAR upload UI callback failed:', e); }
    };
    var cleanup = function() { try { i.remove(); } catch (e) {} };

    i.onchange = function() {
      var f = i.files && i.files[0];
      if (!f) { cleanup(); return; }
      var maxZipBytes = 64 * 1024 * 1024;
      if (f.size > maxZipBytes) {
        send('ERR|413|The experiment ZIP exceeds the 64 MiB upload limit.');
        cleanup();
        return;
      }
      send('START|' + f.name + '|' + f.size);

      try {
        var fd = new FormData();
        fd.append('file', f, f.name);
        var xhr = new XMLHttpRequest();
        xhr.open('POST', '/api/admin/experiments/upload', true);
        xhr.withCredentials = true;
        xhr.upload.onprogress = function(ev) {
          if (ev.lengthComputable && ev.total > 0) {
            var pct = Math.max(0, Math.min(100, Math.round((ev.loaded / ev.total) * 100)));
            send('PROGRESS|' + pct + '|' + ev.loaded + '|' + ev.total);
          }
        };
        xhr.upload.onload = function() { send('PROCESSING|Upload complete. Validating and installing experiment...'); };
        xhr.onload = function() {
          var body = xhr.responseText || '';
          if (xhr.status >= 200 && xhr.status < 300) send('OK|' + body);
          else if (xhr.status === 413 && !body) send('ERR|413|The experiment ZIP is too large for the server upload limit.');
          else send('ERR|' + xhr.status + '|' + body);
          cleanup();
        };
        xhr.onerror = function() { send('ERR|0|Network error while uploading experiment ZIP.'); cleanup(); };
        xhr.onabort = function() { send('ERR|0|Upload cancelled.'); cleanup(); };
        xhr.send(fd);
      } catch (e) {
        send('ERR|0|' + e.toString());
        cleanup();
      }
    };
    i.click();
  }});
