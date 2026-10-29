using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace RevitTrueGltf.Live
{
    internal sealed class LiveSession
    {
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(1500);

        public static LiveSession Current { get; private set; }

        private readonly UIApplication _uiApp;
        private readonly Document _doc;
        private readonly ElementId _viewId;
        private readonly string _liveDir;
        private readonly Queue<string> _oldModels = new Queue<string>();
        private LiveServer _server;
        private bool _dirty;
        private DateTime _lastChangeUtc;
        private int _counter;

        private LiveSession(UIApplication uiApp, Document doc, View3D view)
        {
            _uiApp = uiApp;
            _doc = doc;
            _viewId = view.Id;
            _liveDir = Path.Combine(Path.GetTempPath(), "RevitTrueGltf", "live");
            Directory.CreateDirectory(_liveDir);
            foreach (var f in Directory.GetFiles(_liveDir, "model_*.glb")) TryDelete(f);
        }

        public bool IsFor(Document doc) => doc != null && _doc.Equals(doc);

        public static LiveSession Start(UIApplication uiApp, Document doc, View3D view)
        {
            string viewerDir = Path.Combine(Path.GetDirectoryName(typeof(LiveSession).Assembly.Location), "viewer");
            string indexFile = Path.Combine(viewerDir, "index.html");
            if (!File.Exists(indexFile))
                throw new FileNotFoundException("Не найден viewer/index.html рядом с RevitTrueGltf.dll", indexFile);

            var session = new LiveSession(uiApp, doc, view);
            string firstModel = session.ExportModel(view);
            session._server = LiveServer.StartOnFreePort(viewerDir, firstModel);

            uiApp.Application.DocumentChanged += session.OnDocumentChanged;
            uiApp.Application.DocumentClosing += session.OnDocumentClosing;
            uiApp.Idling += session.OnIdling;

            Current = session;
            session.OpenBrowser();
            return session;
        }

        public void OpenBrowser()
        {
            Process.Start(new ProcessStartInfo(_server.Url) { UseShellExecute = true });
        }

        public void Stop()
        {
            _uiApp.Application.DocumentChanged -= OnDocumentChanged;
            _uiApp.Application.DocumentClosing -= OnDocumentClosing;
            _uiApp.Idling -= OnIdling;
            if (_server != null)
            {
                _server.NotifyStatus("stopped");
                _server.Dispose();
                _server = null;
            }
            if (Current == this) Current = null;
        }

        private void OnDocumentChanged(object sender, DocumentChangedEventArgs e)
        {
            if (!_doc.Equals(e.GetDocument())) return;
            _dirty = true;
            _lastChangeUtc = DateTime.UtcNow;
            _server?.NotifyStatus("changed");
        }

        private void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            if (_doc.Equals(e.Document)) Stop();
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            if (!_dirty || _server == null) return;
            if (DateTime.UtcNow - _lastChangeUtc < Debounce)
            {
                e.SetRaiseWithoutDelay();
                return;
            }
            _dirty = false;

            var view = _doc.GetElement(_viewId) as View3D;
            if (view == null)
            {
                Stop();
                return;
            }

            try
            {
                _server.NotifyStatus("exporting");
                var sw = Stopwatch.StartNew();
                string path = ExportModel(view);
                _server.SetModel(path);
                _server.NotifyStatus("ok " + sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _server?.NotifyStatus("error " + ex.Message.Replace('\r', ' ').Replace('\n', ' '));
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private string ExportModel(View3D view)
        {
            string path = Path.Combine(_liveDir, $"model_{++_counter}.glb");
            var settings = new ExportSettings
            {
                ExportFilePath = path,
                ExportRevitParameters = false
            };
            var context = new ExportGltfContext(_doc, settings);
            using (var exporter = new CustomExporter(_doc, context))
            {
                exporter.IncludeGeometricObjects = false;
                exporter.ShouldStopOnError = false;
                exporter.Export((View)view);
            }

            _oldModels.Enqueue(path);
            while (_oldModels.Count > 3) TryDelete(_oldModels.Dequeue());
            return path;
        }

        private static void TryDelete(string file)
        {
            try { File.Delete(file); } catch { }
        }
    }
}
