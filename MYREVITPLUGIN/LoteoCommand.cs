using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MYREVITPLUGIN
{
    [Transaction(TransactionMode.Manual)]
    public class LoteoCommand : IExternalCommand
    {
        private static LoteoWindow _window;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;

            if (_window != null)
            {
                if (_window.IsLoaded)
                {
                    _window.Activate();
                    return Result.Succeeded;
                }

                _window = null;
            }

            LoteoEngine engine = new LoteoEngine();
            LoteoExternalEventHandler handler = new LoteoExternalEventHandler(engine);
            ExternalEvent externalEvent = ExternalEvent.Create(handler);

            _window = new LoteoWindow(uiapp, engine, externalEvent, handler);
            _window.Closed += (s, e) =>
            {
                externalEvent.Dispose();
                _window = null;
            };

            _window.Show();
            _window.Activate();

            return Result.Succeeded;
        }
    }
}
