using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitTrueGltf.Live
{
    internal sealed class LiveServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _viewerDir;
        private readonly List<HttpListenerResponse> _clients = new List<HttpListenerResponse>();
        private readonly object _lock = new object();
        private readonly Timer _ping;
        private volatile string _modelPath;
        private int _version = 1;
        private volatile bool _disposed;

        public string Url { get; }

        private LiveServer(HttpListener listener, string url, string viewerDir, string modelPath)
        {
            _listener = listener;
            Url = url;
            _viewerDir = Path.GetFullPath(viewerDir);
            _modelPath = modelPath;
            _ping = new Timer(_ => Broadcast(": ping\n\n"), null, 15000, 15000);
            Task.Run(AcceptLoop);
        }

        public static LiveServer StartOnFreePort(string viewerDir, string modelPath)
        {
            for (int port = 47100; port < 47120; port++)
            {
                string url = $"http://localhost:{port}/";
                var listener = new HttpListener();
                listener.Prefixes.Add(url);
                try
                {
                    listener.Start();
                    return new LiveServer(listener, url, viewerDir, modelPath);
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
            }
            throw new InvalidOperationException("Не удалось открыть порт 47100-47119 для Live Viewer.");
        }

        public void SetModel(string path)
        {
            _modelPath = path;
            int v = Interlocked.Increment(ref _version);
            Broadcast(Event("version", v.ToString()));
        }

        public void NotifyStatus(string status) => Broadcast(Event("status", status));

        private static string Event(string name, string data) => $"event: {name}\ndata: {data}\n\n";

        private async Task AcceptLoop()
        {
            while (!_disposed)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (_disposed || !_listener.IsListening) return;
                    continue;
                }
                _ = Task.Run(() => Handle(ctx));
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var res = ctx.Response;
            try
            {
                res.Headers["Cache-Control"] = "no-store";
                string path = ctx.Request.Url.AbsolutePath;

                if (path == "/events")
                {
                    res.ContentType = "text/event-stream; charset=utf-8";
                    res.SendChunked = true;
                    Write(res, Event("version", Volatile.Read(ref _version).ToString()));
                    lock (_lock) _clients.Add(res);
                    return;
                }

                if (path == "/model.glb")
                {
                    ServeFile(res, _modelPath, "model/gltf-binary");
                    return;
                }

                if (path == "/") path = "/index.html";
                string file = Path.GetFullPath(Path.Combine(_viewerDir, Uri.UnescapeDataString(path.TrimStart('/'))));
                if (!file.StartsWith(_viewerDir, StringComparison.OrdinalIgnoreCase))
                {
                    res.StatusCode = 403;
                    res.Close();
                    return;
                }
                ServeFile(res, file, Mime(file));
            }
            catch
            {
                try { res.Abort(); } catch { }
            }
        }

        private static void ServeFile(HttpListenerResponse res, string file, string mime)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                res.StatusCode = 404;
                res.Close();
                return;
            }
            byte[] data;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                data = new byte[fs.Length];
                int read = 0;
                while (read < data.Length)
                {
                    int n = fs.Read(data, read, data.Length - read);
                    if (n == 0) break;
                    read += n;
                }
            }
            res.ContentType = mime;
            res.ContentLength64 = data.Length;
            res.OutputStream.Write(data, 0, data.Length);
            res.Close();
        }

        private static string Mime(string file)
        {
            switch (Path.GetExtension(file).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json";
                case ".png": return "image/png";
                case ".hdr": return "application/octet-stream";
                default: return "application/octet-stream";
            }
        }

        private void Broadcast(string payload)
        {
            lock (_lock)
            {
                for (int i = _clients.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        Write(_clients[i], payload);
                    }
                    catch
                    {
                        try { _clients[i].Abort(); } catch { }
                        _clients.RemoveAt(i);
                    }
                }
            }
        }

        private static void Write(HttpListenerResponse res, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.OutputStream.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ping.Dispose();
            lock (_lock)
            {
                foreach (var c in _clients)
                {
                    try { c.Abort(); } catch { }
                }
                _clients.Clear();
            }
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
        }
    }
}
