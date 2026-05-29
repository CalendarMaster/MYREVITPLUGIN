using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MYREVITPLUGIN
{
    [Transaction(TransactionMode.Manual)]
    public class ExportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uidoc = commandData.Application.ActiveUIDocument;
            var doc = uidoc.Document;

            TaskDialog.Show("M.I. Studio - Exportador de Vistas", "Bienvenido al exportador de planos automatizado de M.I. Studio\n\nEsta herramienta te permitirá exportar tus vistas de Revit de manera rápida y eficiente.");

            return Result.Succeeded;
        }
    }
}
