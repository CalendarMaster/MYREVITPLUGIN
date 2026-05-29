using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Policy;
using System.Text;  
using System.Threading.Tasks;
using System.Windows.Media.Imaging;


namespace MYREVITPLUGIN
{
    public class App : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            RibbonPanel ribbonPanel = application.CreateRibbonPanel("M.I. Studio");

            string thisAssemblyPath = Assembly.GetExecutingAssembly().Location;
            string assemblyDirectory = System.IO.Path.GetDirectoryName(thisAssemblyPath);

            string logoLargePath = System.IO.Path.Combine(assemblyDirectory, "logo_large.png");
            string logoSmallPath = System.IO.Path.Combine(assemblyDirectory, "logo_small.png");

            PushButtonData buttonData = new PushButtonData("cmdExportarVistas", "Exportador\nde Vistas", thisAssemblyPath, "MYREVITPLUGIN.ExportCommand");
            PushButton pushButton = ribbonPanel.AddItem(buttonData) as PushButton;

            pushButton.ToolTip = "Exportador de Planos MI";
            pushButton.LongDescription = "Plugin para exportar planos de manera automatizada. Permite exportar vistas de Revit en múltiples formatos";

            PushButtonData loteoButtonData = new PushButtonData("cmdLoteo", "Loteo\nAutomático", thisAssemblyPath, "MYREVITPLUGIN.LoteoCommand");
            PushButton loteoButton = ribbonPanel.AddItem(loteoButtonData) as PushButton;
            loteoButton.ToolTip = "Automatiza el proceso de loteo desde DWG linkeado";
            loteoButton.LongDescription = "Crea volúmenes de lotes, asigna nombres desde textos CAD y completa el parámetro ID-Lote en elementos del modelo.";

            try
            {
                if (System.IO.File.Exists(logoLargePath) && System.IO.File.Exists(logoSmallPath))
                {
                    BitmapImage largeBitmapImage = new BitmapImage(new Uri(logoLargePath));
                    BitmapImage smallBitmapImage = new BitmapImage(new Uri(logoSmallPath));

                    pushButton.LargeImage = largeBitmapImage;
                    pushButton.Image = smallBitmapImage;
                    loteoButton.LargeImage = largeBitmapImage;
                    loteoButton.Image = smallBitmapImage;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Archivos de imagen no encontrados");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error cargando imagen: " + ex.Message);
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }
    }


}