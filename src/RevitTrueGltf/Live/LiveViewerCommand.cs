using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitTrueGltf.Utils;
using System;
using System.Runtime.CompilerServices;

namespace RevitTrueGltf.Live
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class LiveViewerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            LiveAssemblyResolver.EnsureRegistered();
            try
            {
                return Run(commandData.Application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Live Viewer", "Ошибка: " + ex.Message);
                message = ex.Message;
                return Result.Failed;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Result Run(UIApplication uiApp)
        {
            LoggingHelper.Initialize();
            Document doc = uiApp.ActiveUIDocument?.Document;

            var current = LiveSession.Current;
            if (current != null)
            {
                bool same = current.IsFor(doc);
                current.Stop();
                if (same)
                {
                    TaskDialog.Show("Live Viewer", "Live Viewer остановлен.");
                    return Result.Succeeded;
                }
            }

            if (!(doc?.ActiveView is View3D view) || view.IsTemplate)
            {
                TaskDialog.Show("Live Viewer", "Откройте 3D-вид и запустите команду снова.");
                return Result.Failed;
            }

            RevitContext.Initialize(uiApp.Application);
            LiveSession.Start(uiApp, doc, view);
            return Result.Succeeded;
        }
    }
}
