using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace MYREVITPLUGIN
{
    public enum LoteoRequestType
    {
        None,
        Step1CreateLots,
        Step2NameLots,
        Step3AssignParameter
    }

    public class LoteoStepResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int TotalCount { get; set; }
        public int NamedCount { get; set; }
        public int UpdatedElements { get; set; }
    }

    public class LoteoExternalEventHandler : IExternalEventHandler
    {
        private readonly LoteoEngine _engine;
        private LoteoWindow _window;

        public LoteoExternalEventHandler(LoteoEngine engine)
        {
            _engine = engine;
        }

        public LoteoRequestType Request { get; set; }
        public string LimitsLayer { get; set; }
        public string TextLayer { get; set; }
        public bool CreateParameterIfMissing { get; set; } = true;

        public void SetWindow(LoteoWindow window)
        {
            _window = window;
        }

        public void Execute(UIApplication app)
        {
            LoteoStepResult result;

            try
            {
                switch (Request)
                {
                    case LoteoRequestType.Step1CreateLots:
                        result = _engine.ExecuteStep1(app, LimitsLayer, TextLayer);
                        _window?.Dispatcher.Invoke(() => _window.OnStep1Completed(result));
                        break;

                    case LoteoRequestType.Step2NameLots:
                        result = _engine.ExecuteStep2(app, TextLayer);
                        _window?.Dispatcher.Invoke(() => _window.OnStep2Completed(result));
                        break;

                    case LoteoRequestType.Step3AssignParameter:
                        result = _engine.ExecuteStep3(app, CreateParameterIfMissing);
                        _window?.Dispatcher.Invoke(() => _window.OnStep3Completed(result));
                        break;
                }
            }
            catch (Exception ex)
            {
                _window?.Dispatcher.Invoke(() => _window.OnStep3Completed(new LoteoStepResult
                {
                    Success = false,
                    Message = "Error inesperado: " + ex.Message
                }));
            }
            finally
            {
                Request = LoteoRequestType.None;
            }
        }

        public string GetName()
        {
            return "Loteo External Event Handler";
        }
    }

    public class DwgLinkSelectionFilter : ISelectionFilter
    {
        private readonly Document _doc;

        public DwgLinkSelectionFilter(Document doc)
        {
            _doc = doc;
        }

        public bool AllowElement(Element elem)
        {
            ImportInstance ii = elem as ImportInstance;
            return ii != null && ii.IsLinked;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    public class LoteoEngine
    {
        private const string ProjectParameterName = "ID-Lote";
        private const double LoteHeightFeet = 300.0;

        private readonly Dictionary<ElementId, string> _lotNamesByDirectShape = new Dictionary<ElementId, string>();
        private readonly Dictionary<ElementId, Solid> _lotSolidCache = new Dictionary<ElementId, Solid>();

        public ElementId SelectedImportId { get; private set; } = ElementId.InvalidElementId;

        public void SetSelectedImport(ElementId importId)
        {
            SelectedImportId = importId;
        }

        public List<string> GetLayersFromImport(Document doc, ImportInstance importInstance)
        {
            var layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GeometryElement geo = importInstance.get_Geometry(new Options());

            if (geo == null)
            {
                return new List<string>();
            }

            foreach (GeometryObject obj in geo)
            {
                CollectLayerNamesRecursive(doc, obj, layerNames);
            }

            return layerNames.OrderBy(x => x).ToList();
        }

        public LoteoStepResult ExecuteStep1(UIApplication uiapp, string limitsLayer, string textLayer)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (SelectedImportId == ElementId.InvalidElementId)
            {
                return Fail("Debe seleccionar un DWG primero.");
            }

            ImportInstance import = doc.GetElement(SelectedImportId) as ImportInstance;
            if (import == null || !import.IsLinked)
            {
                return Fail("El DWG seleccionado no es válido o ya no existe.");
            }

            GeometryElement geo = import.get_Geometry(new Options());
            if (geo == null)
            {
                return Fail("No se pudo leer la geometría del DWG linkeado.");
            }

            Transform transform = import.GetTransform();

            int created = 0;
            _lotNamesByDirectShape.Clear();
            _lotSolidCache.Clear();

            using (Transaction tx = new Transaction(doc, "Paso 1 - Crear lotes DirectShape"))
            {
                tx.Start();

                foreach (GeometryObject obj in geo)
                {
                    foreach (PolyLine polyline in CollectPolylinesFromGeometry(doc, obj, limitsLayer))
                    {
                        CurveLoop loop = BuildClosedCurveLoopFromPolyline(polyline, transform);
                        if (loop == null)
                        {
                            continue;
                        }

                        try
                        {
                            IList<CurveLoop> loops = new List<CurveLoop> { loop };
                            Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, LoteHeightFeet);

                            DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                            ds.Name = "Lote_sin_nombre";
                            ds.SetShape(new GeometryObject[] { solid });

                            _lotNamesByDirectShape[ds.Id] = string.Empty;
                            _lotSolidCache[ds.Id] = solid;
                            created++;
                        }
                        catch
                        {
                            // Omite geometría inválida puntual
                        }
                    }
                }

                tx.Commit();
            }

            return new LoteoStepResult
            {
                Success = true,
                Message = "Paso 1 completado.",
                TotalCount = created
            };
        }

        public LoteoStepResult ExecuteStep2(UIApplication uiapp, string textLayer)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (SelectedImportId == ElementId.InvalidElementId)
            {
                return Fail("Debe seleccionar un DWG primero.");
            }

            if (_lotNamesByDirectShape.Count == 0)
            {
                return Fail("No hay lotes creados. Ejecute el Paso 1 primero.");
            }

            ImportInstance import = doc.GetElement(SelectedImportId) as ImportInstance;
            if (import == null)
            {
                return Fail("No se encontró el DWG seleccionado.");
            }

            string dwgPath = GetDwgAbsolutePath(import);
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            {
                return Fail("No se pudo obtener la ruta del DWG original en disco.");
            }

            Transform transform = import.GetTransform();
            var cadTexts = ReadDwgTexts(dwgPath, textLayer, transform);
            if (cadTexts.Count == 0)
            {
                return Fail("No se encontraron textos en la capa seleccionada del DWG.");
            }

            int named = 0;

            using (Transaction tx = new Transaction(doc, "Paso 2 - Nombrar lotes"))
            {
                tx.Start();

                foreach (var textItem in cadTexts)
                {
                    foreach (var pair in _lotNamesByDirectShape.ToList())
                    {
                        DirectShape ds = doc.GetElement(pair.Key) as DirectShape;
                        if (ds == null)
                        {
                            continue;
                        }

                        Solid solid = GetDirectShapeSolid(ds);
                        if (solid == null)
                        {
                            continue;
                        }

                        if (IsPointInsideSolidApprox(solid, textItem.Point))
                        {
                            ds.Name = textItem.Value;
                            _lotNamesByDirectShape[pair.Key] = textItem.Value;
                            named++;
                            break;
                        }
                    }
                }

                tx.Commit();
            }

            return new LoteoStepResult
            {
                Success = true,
                Message = "Paso 2 completado.",
                TotalCount = _lotNamesByDirectShape.Count,
                NamedCount = named
            };
        }

        public LoteoStepResult ExecuteStep3(UIApplication uiapp, bool createParameterIfMissing)
        {
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            if (_lotNamesByDirectShape.Count == 0)
            {
                return Fail("No hay lotes para asignar. Ejecute los pasos previos.");
            }

            int updated = 0;

            using (Transaction tx = new Transaction(doc, "Paso 3 - Asignar ID-Lote"))
            {
                tx.Start();

                if (createParameterIfMissing)
                {
                    EnsureProjectParameterIdLote(doc);
                }

                foreach (var pair in _lotNamesByDirectShape)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Equals("Lote_sin_nombre", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DirectShape lotDs = doc.GetElement(pair.Key) as DirectShape;
                    if (lotDs == null)
                    {
                        continue;
                    }

                    Solid lotSolid = GetDirectShapeSolid(lotDs);
                    if (lotSolid == null)
                    {
                        continue;
                    }

                    BoundingBoxXYZ lotBbox = lotDs.get_BoundingBox(null);
                    if (lotBbox == null)
                    {
                        continue;
                    }

                    Outline outline = new Outline(lotBbox.Min, lotBbox.Max);
                    BoundingBoxIntersectsFilter bboxFilter = new BoundingBoxIntersectsFilter(outline);

                    FilteredElementCollector collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .WherePasses(bboxFilter);

                    foreach (Element element in collector)
                    {
                        if (element.Id == lotDs.Id || element.Id == SelectedImportId)
                        {
                            continue;
                        }

                        if (element is DirectShape && _lotNamesByDirectShape.ContainsKey(element.Id))
                        {
                            continue;
                        }

                        XYZ testPoint = GetElementTestPoint(element);
                        if (testPoint == null)
                        {
                            continue;
                        }

                        if (!IsPointInsideSolidApprox(lotSolid, testPoint))
                        {
                            continue;
                        }

                        Parameter p = element.LookupParameter(ProjectParameterName);
                        if (p != null && !p.IsReadOnly)
                        {
                            p.Set(pair.Value);
                            updated++;
                        }
                    }
                }

                tx.Commit();
            }

            return new LoteoStepResult
            {
                Success = true,
                Message = "Paso 3 completado.",
                UpdatedElements = updated
            };
        }

        private static void CollectLayerNamesRecursive(Document doc, GeometryObject obj, HashSet<string> layerNames)
        {
            string layer = GetLayerName(doc, obj);
            if (!string.IsNullOrWhiteSpace(layer))
            {
                layerNames.Add(layer);
            }

            GeometryInstance gi = obj as GeometryInstance;
            if (gi == null)
            {
                return;
            }

            GeometryElement symbolGeo = gi.GetSymbolGeometry();
            if (symbolGeo == null)
            {
                return;
            }

            foreach (GeometryObject nested in symbolGeo)
            {
                CollectLayerNamesRecursive(doc, nested, layerNames);
            }
        }

        private static IEnumerable<PolyLine> CollectPolylinesFromGeometry(Document doc, GeometryObject obj, string targetLayer)
        {
            var result = new List<PolyLine>();

            if (obj is PolyLine pl && IsObjectOnLayer(doc, pl, targetLayer))
            {
                result.Add(pl);
            }

            GeometryInstance gi = obj as GeometryInstance;
            if (gi != null)
            {
                GeometryElement symbolGeo = gi.GetSymbolGeometry();
                if (symbolGeo != null)
                {
                    foreach (GeometryObject nested in symbolGeo)
                    {
                        result.AddRange(CollectPolylinesFromGeometry(doc, nested, targetLayer));
                    }
                }
            }

            return result;
        }

        private static CurveLoop BuildClosedCurveLoopFromPolyline(PolyLine polyline, Transform importTransform)
        {
            IList<XYZ> points = polyline.GetCoordinates();
            if (points == null || points.Count < 3)
            {
                return null;
            }

            var transformed = points
                .Select(p => importTransform.OfPoint(p))
                .Select(p => new XYZ(p.X, p.Y, p.Z - LoteHeightFeet))
                .ToList();

            XYZ first = transformed.First();
            XYZ last = transformed.Last();
            if (!first.IsAlmostEqualTo(last))
            {
                transformed.Add(first);
            }

            if (transformed.Count < 4)
            {
                return null;
            }

            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < transformed.Count - 1; i++)
            {
                XYZ p1 = transformed[i];
                XYZ p2 = transformed[i + 1];
                if (p1.DistanceTo(p2) < 1e-7)
                {
                    continue;
                }

                loop.Append(Line.CreateBound(p1, p2));
            }

            return loop;
        }

        private static bool IsObjectOnLayer(Document doc, GeometryObject obj, string targetLayer)
        {
            string layer = GetLayerName(doc, obj);
            return !string.IsNullOrWhiteSpace(layer) && layer.Equals(targetLayer, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLayerName(Document doc, GeometryObject obj)
        {
            if (obj == null || obj.GraphicsStyleId == ElementId.InvalidElementId)
            {
                return null;
            }

            GraphicsStyle style = doc.GetElement(obj.GraphicsStyleId) as GraphicsStyle;
            return style?.GraphicsStyleCategory?.Name;
        }

        private static string GetDwgAbsolutePath(ImportInstance import)
        {
            ExternalFileReference extRef = import.GetExternalFileReference();
            if (extRef == null)
            {
                return null;
            }

            ModelPath path = extRef.GetPath();
            if (path == null)
            {
                return null;
            }

            return ModelPathUtils.ConvertModelPathToUserVisiblePath(path);
        }

        private static List<CadTextPoint> ReadDwgTexts(string dwgPath, string textLayer, Transform importTransform)
        {
            var result = new List<CadTextPoint>();

            try
            {
                Type dxfDocType = Type.GetType("netDxf.DxfDocument, netDxf");
                if (dxfDocType == null)
                {
                    return result;
                }

                MethodInfo loadMethod = dxfDocType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                object dxfDoc = loadMethod?.Invoke(null, new object[] { dwgPath });
                if (dxfDoc == null)
                {
                    return result;
                }

                ExtractCadTexts(dxfDoc, "Texts", textLayer, importTransform, result);
                ExtractCadTexts(dxfDoc, "MTexts", textLayer, importTransform, result);
            }
            catch
            {
                return result;
            }

            return result;
        }

        private static void ExtractCadTexts(object dxfDoc, string collectionPropertyName, string textLayer, Transform importTransform, List<CadTextPoint> result)
        {
            PropertyInfo collectionProperty = dxfDoc.GetType().GetProperty(collectionPropertyName, BindingFlags.Public | BindingFlags.Instance);
            IEnumerable collection = collectionProperty?.GetValue(dxfDoc) as IEnumerable;
            if (collection == null)
            {
                return;
            }

            foreach (object entity in collection)
            {
                if (entity == null)
                {
                    continue;
                }

                object layerObj = entity.GetType().GetProperty("Layer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity);
                string layerName = layerObj?.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)?.GetValue(layerObj) as string;
                if (string.IsNullOrWhiteSpace(layerName) || !layerName.Equals(textLayer, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                object positionObj = entity.GetType().GetProperty("Position", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity)
                    ?? entity.GetType().GetProperty("InsertionPoint", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity);
                if (positionObj == null)
                {
                    continue;
                }

                double x = Convert.ToDouble(positionObj.GetType().GetProperty("X", BindingFlags.Public | BindingFlags.Instance)?.GetValue(positionObj) ?? 0.0);
                double y = Convert.ToDouble(positionObj.GetType().GetProperty("Y", BindingFlags.Public | BindingFlags.Instance)?.GetValue(positionObj) ?? 0.0);
                double z = Convert.ToDouble(positionObj.GetType().GetProperty("Z", BindingFlags.Public | BindingFlags.Instance)?.GetValue(positionObj) ?? 0.0);

                string value = entity.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) as string
                    ?? entity.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) as string
                    ?? entity.GetType().GetProperty("PlainText", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entity) as string;

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                XYZ cadPoint = new XYZ(x, y, z);
                XYZ modelPoint = importTransform.OfPoint(cadPoint);

                result.Add(new CadTextPoint
                {
                    Value = value,
                    Point = modelPoint
                });
            }
        }

        private Solid GetDirectShapeSolid(DirectShape ds)
        {
            if (_lotSolidCache.TryGetValue(ds.Id, out Solid cached) && cached != null)
            {
                return cached;
            }

            GeometryElement ge = ds.get_Geometry(new Options());
            if (ge == null)
            {
                return null;
            }

            foreach (GeometryObject obj in ge)
            {
                Solid solid = ExtractSolidRecursive(obj);
                if (solid != null)
                {
                    _lotSolidCache[ds.Id] = solid;
                    return solid;
                }
            }

            return null;
        }

        private static Solid ExtractSolidRecursive(GeometryObject obj)
        {
            if (obj is Solid s && s.Volume > 0)
            {
                return s;
            }

            GeometryInstance gi = obj as GeometryInstance;
            if (gi == null)
            {
                return null;
            }

            GeometryElement symbolGeo = gi.GetSymbolGeometry();
            if (symbolGeo == null)
            {
                return null;
            }

            foreach (GeometryObject nested in symbolGeo)
            {
                Solid solid = ExtractSolidRecursive(nested);
                if (solid != null)
                {
                    return solid;
                }
            }

            return null;
        }

        private static bool IsPointInsideSolidApprox(Solid solid, XYZ point)
        {
            if (solid == null || point == null)
            {
                return false;
            }

            BoundingBoxXYZ bb = solid.GetBoundingBox();
            if (bb == null)
            {
                return false;
            }

            if (point.X < bb.Min.X || point.X > bb.Max.X ||
                point.Y < bb.Min.Y || point.Y > bb.Max.Y ||
                point.Z < bb.Min.Z || point.Z > bb.Max.Z)
            {
                return false;
            }

            Line ray = Line.CreateBound(point, point + XYZ.BasisX.Multiply(1000000));
            SolidCurveIntersectionOptions options = new SolidCurveIntersectionOptions();
            SolidCurveIntersection sci = solid.IntersectWithCurve(ray, options);
            return sci != null && sci.SegmentCount % 2 == 1;
        }

        private static XYZ GetElementTestPoint(Element element)
        {
            if (element.Location is LocationPoint lp)
            {
                return lp.Point;
            }

            if (element.Location is LocationCurve lc)
            {
                Curve c = lc.Curve;
                if (c != null)
                {
                    return c.Evaluate(0.5, true);
                }
            }

            BoundingBoxXYZ bb = element.get_BoundingBox(null);
            if (bb == null)
            {
                return null;
            }

            return (bb.Min + bb.Max) / 2.0;
        }

        private static void EnsureProjectParameterIdLote(Document doc)
        {
            if (ProjectParameterExists(doc, ProjectParameterName))
            {
                return;
            }

            Application app = doc.Application;
            string originalSpPath = app.SharedParametersFilename;
            string tempSpPath = Path.Combine(Path.GetTempPath(), "MYREVITPLUGIN_ID_LOTE.txt");

            if (!File.Exists(tempSpPath))
            {
                File.WriteAllText(tempSpPath, "# Shared parameter file generated by MYREVITPLUGIN");
            }

            app.SharedParametersFilename = tempSpPath;

            try
            {
                DefinitionFile defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    throw new InvalidOperationException("No se pudo abrir el archivo temporal de parámetros compartidos.");
                }

                DefinitionGroup group = defFile.Groups.get_Item("MYREVITPLUGIN") ?? defFile.Groups.Create("MYREVITPLUGIN");

                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(ProjectParameterName, SpecTypeId.String.Text);
                Definition definition = group.Definitions.get_Item(ProjectParameterName) ?? group.Definitions.Create(options);

                CategorySet categorySet = app.Create.NewCategorySet();
                Categories categories = doc.Settings.Categories;

                foreach (Category category in categories)
                {
                    if (category == null || !category.AllowsBoundParameters || category.IsTagCategory)
                    {
                        continue;
                    }

                    categorySet.Insert(category);
                }

                if (categorySet.IsEmpty)
                {
                    throw new InvalidOperationException("No hay categorías válidas para aplicar el parámetro ID-Lote.");
                }

                InstanceBinding binding = app.Create.NewInstanceBinding(categorySet);
                BindingMap map = doc.ParameterBindings;
                map.Insert(definition, binding, GroupTypeId.IdentityData);
            }
            finally
            {
                app.SharedParametersFilename = originalSpPath;
            }
        }

        private static bool ProjectParameterExists(Document doc, string parameterName)
        {
            BindingMap map = doc.ParameterBindings;
            DefinitionBindingMapIterator it = map.ForwardIterator();
            it.Reset();

            while (it.MoveNext())
            {
                Definition def = it.Key;
                if (def != null && def.Name.Equals(parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static LoteoStepResult Fail(string message)
        {
            return new LoteoStepResult
            {
                Success = false,
                Message = message
            };
        }

        private class CadTextPoint
        {
            public string Value { get; set; }
            public XYZ Point { get; set; }
        }
    }
}
