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
using System.Text;

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
        public string DiagnosticInfo { get; set; }
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
        public string ManualDwgPath { get; set; }
        public bool PreferManualDwgPath { get; set; }
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
                        result = _engine.ExecuteStep2(app, TextLayer, ManualDwgPath, PreferManualDwgPath);
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
            // Allow BOTH linked AND imported DWG instances
            return ii != null;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return true;
        }
    }

    public class LoteoEngine
    {
        private const string ProjectParameterName = "ID-Lote";
        private const string LotNameParameterName = "NOMBRE_LOTE";
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
            Options options = new Options
            {
                IncludeNonVisibleObjects = true
            };

            GeometryElement geo = importInstance.get_Geometry(options);

            if (geo == null)
            {
                return new List<string>();
            }

            foreach (GeometryObject obj in geo)
            {
                CollectLayerNamesRecursive(doc, obj, layerNames, null);
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
            if (import == null)
            {
                return Fail("El DWG seleccionado no es válido o ya no existe.");
            }

            Options geometryOptions = new Options
            {
                IncludeNonVisibleObjects = true
            };

            GeometryElement geo = import.get_Geometry(geometryOptions);
            if (geo == null)
            {
                return Fail("No se pudo leer la geometría del DWG linkeado.");
            }

            // Usar el transform del ImportInstance para crear los lotes en la misma ubicación
            Transform transform = import.GetTransform();

            int created = 0;
            int totalPolylinesDetected = 0;
            int selectedLayerCandidates = 0;
            int discardedInvalidLoop = 0;
            int discardedInvalidSolid = 0;
            var polylinesByLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            _lotNamesByDirectShape.Clear();
            _lotSolidCache.Clear();

            var allPolylines = new List<PolylineWithLayer>();
            foreach (GeometryObject obj in geo)
            {
                allPolylines.AddRange(CollectPolylinesFromGeometry(doc, obj, null, null));
            }

            foreach (var item in allPolylines)
            {
                totalPolylinesDetected++;
                if (!polylinesByLayer.ContainsKey(item.Layer))
                {
                    polylinesByLayer[item.Layer] = 0;
                }

                polylinesByLayer[item.Layer]++;
            }

            using (Transaction tx = new Transaction(doc, "Paso 1 - Crear lotes DirectShape"))
            {
                tx.Start();

                // Limpiar lotes DirectShape existentes creados por esta herramienta
                DeleteExistingLotDirectShapes(doc);

                // Crear ambos parámetros necesarios para el flujo completo
                EnsureProjectParameter(doc, LotNameParameterName);
                EnsureProjectParameterIdLote(doc);

                foreach (var item in allPolylines)
                {
                    if (!item.Layer.Equals(limitsLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    selectedLayerCandidates++;

                    CurveLoop loop = BuildClosedCurveLoopFromPolyline(item.Polyline, transform);
                    if (loop == null)
                    {
                        discardedInvalidLoop++;
                        continue;
                    }

                    try
                    {
                        IList<CurveLoop> loops = new List<CurveLoop> { loop };
                        Solid solid = null;

                        try
                        {
                            solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, LoteHeightFeet);
                        }
                        catch
                        {
                            // Algunos loops válidos geométricamente fallan por orientación; se reintenta invertido.
                            CurveLoop inverted = CurveLoop.CreateViaOffset(loop, 0.0, XYZ.BasisZ.Negate());
                            solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { inverted }, XYZ.BasisZ, LoteHeightFeet);
                        }

                        DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                        ds.Name = "Lote_sin_nombre";
                        ds.SetShape(new GeometryObject[] { solid });

                        Parameter loteNameParam = ds.LookupParameter(LotNameParameterName);
                        if (loteNameParam != null && !loteNameParam.IsReadOnly)
                        {
                            loteNameParam.Set(string.Empty);
                        }

                        _lotNamesByDirectShape[ds.Id] = string.Empty;
                        _lotSolidCache[ds.Id] = solid;
                        created++;
                    }
                    catch
                    {
                        discardedInvalidSolid++;
                    }
                }

                tx.Commit();
            }

            var orderedLayers = polylinesByLayer
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .ToList();

            string layerDetail = orderedLayers.Count == 0
                ? "(sin capas detectadas)"
                : string.Join(Environment.NewLine, orderedLayers.Select(x => "- " + x.Key + ": " + x.Value));

            string diagnostics =
                "[Diagnóstico técnico - Paso 1]" + Environment.NewLine +
                "Capa de límites seleccionada: " + limitsLayer + Environment.NewLine +
                "Total de polylines detectadas en DWG: " + totalPolylinesDetected + Environment.NewLine +
                "Polylines candidatas en capa seleccionada: " + selectedLayerCandidates + Environment.NewLine +
                "Descartadas por loop inválido: " + discardedInvalidLoop + Environment.NewLine +
                "Descartadas por error al crear sólido: " + discardedInvalidSolid + Environment.NewLine +
                "DirectShapes creados: " + created + Environment.NewLine +
                Environment.NewLine +
                "Polylines detectadas por capa:" + Environment.NewLine +
                layerDetail;

            return new LoteoStepResult
            {
                Success = true,
                Message = "Paso 1 completado.",
                TotalCount = created,
                DiagnosticInfo = diagnostics
            };
        }

        public LoteoStepResult ExecuteStep2(UIApplication uiapp, string textLayer, string manualDwgPath = null, bool preferManualDwgPath = false)
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

            Transform transform = import.GetTransform();
            string textDiagnostics;
            List<CadTextPoint> cadTexts;

            // NUEVA ESTRATEGIA: Leer textos directamente del ImportInstance en Revit
            // Esto evita todos los problemas de transformación de coordenadas

            // Primero, intentamos usar el DXF manual si está disponible
            if (preferManualDwgPath && !string.IsNullOrWhiteSpace(manualDwgPath) && File.Exists(manualDwgPath))
            {
                string ext = Path.GetExtension(manualDwgPath).ToUpperInvariant();
                if (ext == ".DXF")
                {
                    // Calcular el offset entre el DXF y el ImportInstance
                    cadTexts = ReadDxfTextsWithAutoCalibration(doc, import, manualDwgPath, textLayer, out textDiagnostics);
                }
                else
                {
                    // Fallback al método antiguo
                    cadTexts = ReadTextsFromImportInstance(doc, import, textLayer, out textDiagnostics);
                }
            }
            else
            {
                // Sin DXF manual, intentar leer del import (probablemente fallará)
                cadTexts = ReadTextsFromImportInstance(doc, import, textLayer, out textDiagnostics);
            }

            /* CÓDIGO ANTIGUO COMENTADO - usar DXF externo causaba problemas de coordenadas
            if (preferManualDwgPath && !string.IsNullOrWhiteSpace(manualDwgPath) && File.Exists(manualDwgPath))
            {
                string ext = Path.GetExtension(manualDwgPath).ToUpperInvariant();
                if (ext == ".DXF")
                {
                    cadTexts = ReadDxfTexts(manualDwgPath, textLayer, transform, out textDiagnostics);
                }
                else
                {
                    cadTexts = ReadDwgTexts(doc, import, textLayer, transform, out textDiagnostics);
                }
            }
            else
            {
                cadTexts = ReadDwgTexts(doc, import, textLayer, transform, out textDiagnostics);
            }
            */

            if (cadTexts.Count == 0)
            {
                return Fail("No se encontraron textos en la capa seleccionada. " + textDiagnostics);
            }

            int named = 0;
            var debugInfo = new StringBuilder();

            using (Transaction tx = new Transaction(doc, "Paso 2 - Nombrar lotes"))
            {
                tx.Start();

                debugInfo.AppendLine($"Intentando nombrar {_lotNamesByDirectShape.Count} lotes con {cadTexts.Count} textos...");

                // Diagnóstico: mostrar el bounding box del primer lote
                if (_lotNamesByDirectShape.Count > 0)
                {
                    var firstPair = _lotNamesByDirectShape.First();
                    DirectShape firstDs = doc.GetElement(firstPair.Key) as DirectShape;
                    if (firstDs != null)
                    {
                        Solid firstSolid = GetDirectShapeSolid(firstDs);
                        if (firstSolid != null)
                        {
                            var bbox = firstSolid.GetBoundingBox();
                            debugInfo.AppendLine($"[Ejemplo] Lote ID {firstDs.Id.IntegerValue} BBox: Min({bbox.Min.X:F2}, {bbox.Min.Y:F2}, {bbox.Min.Z:F2}) Max({bbox.Max.X:F2}, {bbox.Max.Y:F2}, {bbox.Max.Z:F2})");

                            // Mostrar coordenadas de vértices reales del sólido
                            foreach (Face face in firstSolid.Faces)
                            {
                                EdgeArrayArray edgeLoops = face.EdgeLoops;
                                if (edgeLoops == null || edgeLoops.Size == 0) continue;

                                foreach (EdgeArray edgeLoop in edgeLoops)
                                {
                                    if (edgeLoop == null || edgeLoop.Size == 0) continue;

                                    Edge firstEdge = edgeLoop.get_Item(0);
                                    if (firstEdge != null)
                                    {
                                        XYZ p1 = firstEdge.Evaluate(0);
                                        debugInfo.AppendLine($"[Coord. Real] Vértice ejemplo del lote: ({p1.X:F2}, {p1.Y:F2}, {p1.Z:F2})");
                                        break;
                                    }
                                }
                                break; // Solo mostrar la primera cara
                            }
                        }
                    }
                }

                foreach (var textItem in cadTexts)
                {
                    debugInfo.AppendLine($"Texto '{textItem.Value}' en posición ({textItem.Point.X:F2}, {textItem.Point.Y:F2}, {textItem.Point.Z:F2})");

                    bool foundMatch = false;
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

                            // Asignar NOMBRE_LOTE
                            Parameter loteNameParam = ds.LookupParameter(LotNameParameterName);
                            if (loteNameParam != null && !loteNameParam.IsReadOnly)
                            {
                                loteNameParam.Set(textItem.Value);
                            }

                            // Asignar ID-Lote también al DirectShape
                            Parameter idLoteParam = ds.LookupParameter(ProjectParameterName);
                            if (idLoteParam != null && !idLoteParam.IsReadOnly)
                            {
                                idLoteParam.Set(textItem.Value);
                            }

                            _lotNamesByDirectShape[pair.Key] = textItem.Value;
                            named++;
                            foundMatch = true;
                            debugInfo.AppendLine($"  ✓ Asignado a DirectShape ID {ds.Id.IntegerValue}");
                            break;
                        }
                    }

                    if (!foundMatch)
                    {
                        debugInfo.AppendLine($"  ✗ No se encontró lote contenedor");
                    }
                }

                tx.Commit();
            }

            textDiagnostics += Environment.NewLine + Environment.NewLine + debugInfo.ToString();

            return new LoteoStepResult
            {
                Success = true,
                Message = "Paso 2 completado.",
                TotalCount = _lotNamesByDirectShape.Count,
                NamedCount = named,
                DiagnosticInfo = textDiagnostics
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

        private static void CollectLayerNamesRecursive(Document doc, GeometryObject obj, HashSet<string> layerNames, string inheritedLayer)
        {
            string currentLayer = GetLayerName(doc, obj) ?? inheritedLayer;
            if (!string.IsNullOrWhiteSpace(currentLayer))
            {
                layerNames.Add(currentLayer);
            }

            GeometryInstance gi = obj as GeometryInstance;
            if (gi == null)
            {
                return;
            }

            GeometryElement symbolGeo = gi.GetSymbolGeometry();
            if (symbolGeo != null)
            {
                foreach (GeometryObject nested in symbolGeo)
                {
                    CollectLayerNamesRecursive(doc, nested, layerNames, currentLayer);
                }
            }

            GeometryElement instanceGeo = gi.GetInstanceGeometry();
            if (instanceGeo != null)
            {
                foreach (GeometryObject nested in instanceGeo)
                {
                    CollectLayerNamesRecursive(doc, nested, layerNames, currentLayer);
                }
            }
        }

        private static IEnumerable<PolylineWithLayer> CollectPolylinesFromGeometry(Document doc, GeometryObject obj, string targetLayer, string inheritedLayer)
        {
            var result = new List<PolylineWithLayer>();
            string currentLayer = GetLayerName(doc, obj) ?? inheritedLayer;

            if (obj is PolyLine pl && !string.IsNullOrWhiteSpace(currentLayer))
            {
                if (string.IsNullOrWhiteSpace(targetLayer) || currentLayer.Equals(targetLayer, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new PolylineWithLayer
                    {
                        Polyline = pl,
                        Layer = currentLayer
                    });
                }
            }

            GeometryInstance gi = obj as GeometryInstance;
            if (gi != null)
            {
                GeometryElement symbolGeo = gi.GetSymbolGeometry();
                if (symbolGeo != null)
                {
                    foreach (GeometryObject nested in symbolGeo)
                    {
                        result.AddRange(CollectPolylinesFromGeometry(doc, nested, targetLayer, currentLayer));
                    }
                }

                GeometryElement instanceGeo = gi.GetInstanceGeometry();
                if (instanceGeo != null)
                {
                    foreach (GeometryObject nested in instanceGeo)
                    {
                        result.AddRange(CollectPolylinesFromGeometry(doc, nested, targetLayer, currentLayer));
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
                .ToList();

            if (transformed.Count < 3)
            {
                return null;
            }

            // Fuerza coplanaridad en Z para evitar fallos de extrusión por pequeñas variaciones del DWG.
            double baseZ = transformed.Min(p => p.Z) - LoteHeightFeet;
            var flattened = transformed
                .Select(p => new XYZ(p.X, p.Y, baseZ))
                .ToList();

            var cleaned = new List<XYZ>();
            foreach (XYZ p in flattened)
            {
                if (cleaned.Count == 0 || cleaned[cleaned.Count - 1].DistanceTo(p) > 1e-6)
                {
                    cleaned.Add(p);
                }
            }

            if (cleaned.Count < 3)
            {
                return null;
            }

            XYZ first = cleaned.First();
            XYZ last = cleaned.Last();
            if (!first.IsAlmostEqualTo(last))
            {
                cleaned.Add(first);
            }

            if (cleaned.Count < 4)
            {
                return null;
            }

            CurveLoop loop = new CurveLoop();
            for (int i = 0; i < cleaned.Count - 1; i++)
            {
                XYZ p1 = cleaned[i];
                XYZ p2 = cleaned[i + 1];
                if (p1.DistanceTo(p2) < 1e-6)
                {
                    continue;
                }

                loop.Append(Line.CreateBound(p1, p2));
            }

            return loop;
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

        private static string GetDwgAbsolutePath(Document doc, ImportInstance import)
        {
            if (doc == null || import == null)
            {
                return null;
            }

            ExternalFileReference extRef = null;

            try
            {
                extRef = import.GetExternalFileReference();
            }
            catch
            {
                // Algunos ImportInstance no exponen referencia externa directa.
            }

            if (extRef == null)
            {
                ElementId typeId = import.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    try
                    {
                        extRef = ExternalFileUtils.GetExternalFileReference(doc, typeId);
                    }
                    catch
                    {
                        // Puede no existir referencia externa para el typeId.
                    }
                }
            }

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

        private List<CadTextPoint> ReadTextsFromImportInstance(Document doc, ImportInstance import, string textLayer, out string diagnostics)
        {
            // NOTA: Revit NO expone el texto real de los elementos CAD importados
            // Debemos usar el archivo DXF externo, pero necesitamos calcular la transformación correcta

            // Por ahora, redirigimos al método ReadDxfTexts con transformación automática
            diagnostics = "ERROR: Se requiere un archivo DXF manual para leer los textos.";
            return new List<CadTextPoint>();
        }

        private List<CadTextPoint> ReadDxfTextsWithAutoCalibration(Document doc, ImportInstance import, string dxfPath, string textLayer, out string diagnostics)
        {
            var result = new List<CadTextPoint>();
            var diag = new StringBuilder();

            diag.AppendLine("[Diagnóstico técnico - Paso 2 - Lectura DXF con transform del import]");
            diag.AppendLine("Archivo: " + Path.GetFileName(dxfPath));
            diag.AppendLine("Capa solicitada: " + textLayer);

            // Obtener el transform del ImportInstance (el mismo que usó Step 1)
            Transform importTransform = import.GetTransform();
            diag.AppendLine($"Transform del import: Origin=({importTransform.Origin.X:F2}, {importTransform.Origin.Y:F2}, {importTransform.Origin.Z:F2})");

            // Leer el DXF
            netDxf.DxfDocument dxfDoc = null;
            try
            {
                dxfDoc = netDxf.DxfDocument.Load(dxfPath);
            }
            catch (Exception ex)
            {
                diag.AppendLine($"ERROR al cargar DXF: {ex.Message}");
                diagnostics = diag.ToString();
                return result;
            }

            if (dxfDoc == null)
            {
                diag.AppendLine("ERROR: No se pudo cargar el archivo DXF.");
                diagnostics = diag.ToString();
                return result;
            }

            diag.AppendLine($"DXF cargado exitosamente. Versión: {dxfDoc.DrawingVariables.AcadVer}");

            // Determinar factor de conversión según las unidades del DXF
            double conversionFactor = 1.0;
            string unitsInfo = "Desconocidas";

            if (dxfDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Centimeters)
            {
                conversionFactor = 1.0 / 30.48; // cm a pies
                unitsInfo = "Centímetros";
            }
            else if (dxfDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Millimeters)
            {
                conversionFactor = 1.0 / 304.8; // mm a pies
                unitsInfo = "Milímetros";
            }
            else if (dxfDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Meters)
            {
                conversionFactor = 1.0 / 0.3048; // m a pies
                unitsInfo = "Metros";
            }
            else if (dxfDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Inches)
            {
                conversionFactor = 1.0 / 12.0; // pulgadas a pies
                unitsInfo = "Pulgadas";
            }
            else if (dxfDoc.DrawingVariables.InsUnits == netDxf.Units.DrawingUnits.Feet)
            {
                conversionFactor = 1.0; // ya está en pies
                unitsInfo = "Pies";
            }

            diag.AppendLine($"Unidades del DXF: {unitsInfo} (factor conversión: {conversionFactor:F6})");

            // Leer textos y aplicar el mismo transform que Step 1
            int totalTextsFound = 0;
            int textsInLayer = 0;
            var textsByLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Debug: mostrar primera posición SIN transformar
            bool firstTextShown = false;

            if (dxfDoc.Entities != null && dxfDoc.Entities.Texts != null)
            {
                foreach (netDxf.Entities.Text text in dxfDoc.Entities.Texts)
                {
                    totalTextsFound++;

                    string layer = text.Layer?.Name ?? "0";
                    if (!textsByLayer.ContainsKey(layer))
                    {
                        textsByLayer[layer] = 0;
                    }
                    textsByLayer[layer]++;

                    if (string.IsNullOrWhiteSpace(textLayer) || layer.Equals(textLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        // Debug: mostrar primera posición cruda del DXF
                        if (!firstTextShown)
                        {
                            diag.AppendLine($"[Debug] Primera posición DXF cruda: ({text.Position.X:F2}, {text.Position.Y:F2}, {text.Position.Z:F2})");
                            firstTextShown = true;
                        }

                        // Convertir de unidades DXF a pies
                        XYZ dxfPosition = new XYZ(
                            text.Position.X * conversionFactor,
                            text.Position.Y * conversionFactor,
                            text.Position.Z * conversionFactor
                        );

                        // Aplicar el transform del import (igual que Step 1)
                        XYZ transformedPosition = importTransform.OfPoint(dxfPosition);

                        result.Add(new CadTextPoint
                        {
                            Value = text.Value?.Trim(),
                            Point = transformedPosition
                        });

                        textsInLayer++;
                    }
                }
            }

            if (dxfDoc.Entities != null && dxfDoc.Entities.MTexts != null)
            {
                foreach (netDxf.Entities.MText mtext in dxfDoc.Entities.MTexts)
                {
                    totalTextsFound++;

                    string layer = mtext.Layer?.Name ?? "0";
                    if (!textsByLayer.ContainsKey(layer))
                    {
                        textsByLayer[layer] = 0;
                    }
                    textsByLayer[layer]++;

                    if (string.IsNullOrWhiteSpace(textLayer) || layer.Equals(textLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        // Convertir de unidades DXF a pies
                        XYZ dxfPosition = new XYZ(
                            mtext.Position.X * conversionFactor,
                            mtext.Position.Y * conversionFactor,
                            mtext.Position.Z * conversionFactor
                        );

                        // Aplicar el transform del import (igual que Step 1)
                        XYZ transformedPosition = importTransform.OfPoint(dxfPosition);

                        result.Add(new CadTextPoint
                        {
                            Value = mtext.Value?.Trim(),
                            Point = transformedPosition
                        });

                        textsInLayer++;
                    }
                }
            }

            var orderedLayers = textsByLayer
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .ToList();

            string layerDetail = orderedLayers.Count == 0
                ? "(sin capas detectadas con textos)"
                : string.Join(Environment.NewLine, orderedLayers.Select(x => "  - " + x.Key + ": " + x.Value));

            diag.AppendLine($"Total de textos en DXF: {totalTextsFound}");
            diag.AppendLine($"Textos en capa '{textLayer}': {textsInLayer}");
            diag.AppendLine("");
            diag.AppendLine("Textos detectados por capa:");
            diag.AppendLine(layerDetail);

            diagnostics = diag.ToString();
            return result;
        }

        private List<CadTextPoint> ReadDwgTexts(Document doc, ImportInstance import, string textLayer, Transform importTransform, out string diagnostics)
        {
            var result = new List<CadTextPoint>();
            var diag = new StringBuilder();

            diag.AppendLine("[Diagnóstico técnico - Paso 2]");
            diag.AppendLine("Método: Lectura de textos desde Revit");
            diag.AppendLine("Capa solicitada: " + textLayer);
            diag.AppendLine("DWG tipo: " + (import.IsLinked ? "Vinculado" : "Importado"));

            int totalTextsFound = 0;
            int textsInLayer = 0;
            int textsNearImport = 0;
            var textsByLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get bounding box of the import to search for nearby texts
                BoundingBoxXYZ importBbox = import.get_BoundingBox(null);

                // Strategy 1: Find ALL TextNote elements in the document
                FilteredElementCollector allTextsCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(TextNote));

                diag.AppendLine($"Total TextNotes en documento: {allTextsCollector.GetElementCount()}");

                foreach (TextNote textNote in allTextsCollector)
                {
                    string textValue = textNote.Text;
                    if (string.IsNullOrWhiteSpace(textValue))
                    {
                        continue;
                    }

                    XYZ textPosition = textNote.Coord;

                    // Check if text is near the import (expanded bounding box)
                    bool isNearImport = true;
                    if (importBbox != null)
                    {
                        double margin = 50.0; // 50 feet margin
                        XYZ expandedMin = importBbox.Min - new XYZ(margin, margin, margin);
                        XYZ expandedMax = importBbox.Max + new XYZ(margin, margin, margin);

                        isNearImport = textPosition.X >= expandedMin.X && textPosition.X <= expandedMax.X &&
                                      textPosition.Y >= expandedMin.Y && textPosition.Y <= expandedMax.Y;
                    }

                    if (!isNearImport)
                    {
                        continue;
                    }

                    textsNearImport++;

                    // Try to get layer info from the text note
                    string layer = TryGetLayerFromElement(doc, textNote);

                    totalTextsFound++;

                    if (!textsByLayer.ContainsKey(layer ?? "Unknown"))
                    {
                        textsByLayer[layer ?? "Unknown"] = 0;
                    }
                    textsByLayer[layer ?? "Unknown"]++;

                    if (string.IsNullOrWhiteSpace(textLayer) ||
                        (layer != null && layer.Equals(textLayer, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add(new CadTextPoint
                        {
                            Value = textValue.Trim(),
                            Point = textPosition
                        });

                        textsInLayer++;
                    }
                }

                // Strategy 2: If no TextNotes found, try parsing geometry
                if (result.Count == 0 && import != null)
                {
                    diag.AppendLine("No se encontraron TextNotes. Intentando parsear geometría del DWG...");

                    Options options = new Options
                    {
                        IncludeNonVisibleObjects = true,
                        DetailLevel = ViewDetailLevel.Fine
                    };

                    GeometryElement geo = import.get_Geometry(options);
                    if (geo != null)
                    {
                        CollectTextsFromGeometry(doc, geo, textLayer, importTransform, result, textsByLayer, ref totalTextsFound, ref textsInLayer);
                    }
                }

                var orderedLayers = textsByLayer
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key)
                    .ToList();

                string layerDetail = orderedLayers.Count == 0
                    ? "(sin capas detectadas con textos)"
                    : string.Join(Environment.NewLine, orderedLayers.Select(x => "  - " + x.Key + ": " + x.Value));

                diag.AppendLine($"Textos cerca del DWG: {textsNearImport}");
                diag.AppendLine($"Total de textos detectados: {totalTextsFound}");
                diag.AppendLine($"Textos en capa '{textLayer}': {textsInLayer}");
                diag.AppendLine("");
                diag.AppendLine("Textos detectados por capa:");
                diag.AppendLine(layerDetail);

                if (result.Count == 0)
                {
                    diag.AppendLine("");
                    diag.AppendLine("⚠️ ADVERTENCIA: No se detectaron textos.");
                    diag.AppendLine("");
                    diag.AppendLine("Posibles causas:");
                    diag.AppendLine("  1. Los textos están en una capa diferente");
                    diag.AppendLine("  2. El DWG no fue explotado en Revit");
                    diag.AppendLine("  3. Los textos son bloques o anotaciones no estándar");
                    diag.AppendLine("");
                    diag.AppendLine("SOLUCIONES:");
                    diag.AppendLine("  → En Revit: Seleccionar DWG → Modify → 'Explode'");
                    diag.AppendLine("  → Verificar que los textos sean visibles en la vista");
                    diag.AppendLine("  → Revisar el nombre de la capa en la lista de capas detectadas");
                }
            }
            catch (Exception ex)
            {
                diag.AppendLine("");
                diag.AppendLine("❌ ERROR CRÍTICO: " + ex.Message);
                diag.AppendLine("Stack: " + ex.StackTrace);
            }

            diagnostics = diag.ToString();
            return result;
        }

        private string TryGetLayerFromElement(Document doc, Element element)
        {
            if (element == null)
            {
                return null;
            }

            // Try to get layer from graphics style (only for geometry objects)
            // TextNote doesn't have GraphicsStyleId, so skip this check

            // Try to get layer from category
            Category cat = element.Category;
            if (cat != null)
            {
                return cat.Name;
            }

            // Try to get layer from parameters
            Parameter layerParam = element.LookupParameter("Layer");
            if (layerParam != null && layerParam.StorageType == StorageType.String)
            {
                string val = layerParam.AsString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }

            return "Unknown";
        }

        private void CollectTextsFromGeometry(
            Document doc,
            GeometryElement geoElement,
            string targetLayer,
            Transform importTransform,
            List<CadTextPoint> result,
            Dictionary<string, int> textsByLayer,
            ref int totalTextsFound,
            ref int textsInLayer)
        {
            if (geoElement == null)
            {
                return;
            }

            foreach (GeometryObject obj in geoElement)
            {
                CollectTextsFromGeometryObject(doc, obj, targetLayer, importTransform, result, textsByLayer, ref totalTextsFound, ref textsInLayer, null);
            }
        }

        private void CollectTextsFromGeometryObject(
            Document doc,
            GeometryObject obj,
            string targetLayer,
            Transform importTransform,
            List<CadTextPoint> result,
            Dictionary<string, int> textsByLayer,
            ref int totalTextsFound,
            ref int textsInLayer,
            string currentLayer)
        {
            if (obj == null)
            {
                return;
            }

            string layer = GetLayerName(doc, obj);
            if (!string.IsNullOrWhiteSpace(layer))
            {
                currentLayer = layer;
            }

            // GeometryInstance represents blocks/symbols in DWG imports
            GeometryInstance gi = obj as GeometryInstance;
            if (gi != null)
            {
                // Get the family symbol to check if it's a text element
                // In DWG imports, text often appears as very simple geometry instances
                GeometryElement symbolGeo = gi.GetSymbolGeometry();
                GeometryElement instanceGeo = gi.GetInstanceGeometry();

                // Check if this might be a text by examining its geometry complexity
                // Text elements typically have minimal geometry (often just lines forming characters)
                bool mightBeText = false;
                XYZ textPosition = gi.Transform.Origin;

                if (symbolGeo != null)
                {
                    int lineCount = 0;
                    foreach (GeometryObject symObj in symbolGeo)
                    {
                        if (symObj is Line || symObj is PolyLine)
                        {
                            lineCount++;
                        }
                    }

                    // Text elements usually have multiple small lines forming characters
                    // This is a heuristic - adjust as needed
                    if (lineCount > 0 && lineCount < 200)
                    {
                        mightBeText = true;
                    }
                }

                // Try to extract text value from the geometry instance
                // This is challenging because Revit doesn't expose the actual text string
                // from DWG imports directly. We need an alternative approach.

                // Recurse into nested geometry regardless
                if (symbolGeo != null)
                {
                    foreach (GeometryObject nested in symbolGeo)
                    {
                        CollectTextsFromGeometryObject(doc, nested, targetLayer, importTransform, result, textsByLayer, ref totalTextsFound, ref textsInLayer, currentLayer);
                    }
                }

                if (instanceGeo != null)
                {
                    foreach (GeometryObject nested in instanceGeo)
                    {
                        CollectTextsFromGeometryObject(doc, nested, targetLayer, importTransform, result, textsByLayer, ref totalTextsFound, ref textsInLayer, currentLayer);
                    }
                }
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

            // Check only X and Y bounds, ignore Z (text may be at different elevation)
            if (point.X < bb.Min.X || point.X > bb.Max.X ||
                point.Y < bb.Min.Y || point.Y > bb.Max.Y)
            {
                return false;
            }

            // Project point to mid-Z of the solid for better 3D matching
            double midZ = (bb.Min.Z + bb.Max.Z) / 2.0;
            XYZ testPoint = new XYZ(point.X, point.Y, midZ);

            Line ray = Line.CreateBound(testPoint, testPoint + XYZ.BasisX.Multiply(1000000));
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

        private static void DeleteExistingLotDirectShapes(Document doc)
        {
            // Buscar todos los DirectShape en el documento
            FilteredElementCollector collector = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape));

            List<ElementId> toDelete = new List<ElementId>();

            foreach (DirectShape ds in collector)
            {
                // Identificar si es un lote creado por esta herramienta
                // (tiene el parámetro NOMBRE_LOTE o su nombre contiene "Lote")
                Parameter nameParam = ds.LookupParameter(LotNameParameterName);
                if (nameParam != null || ds.Name.Contains("Lote") || ds.Name.Contains("lote"))
                {
                    toDelete.Add(ds.Id);
                }
            }

            if (toDelete.Count > 0)
            {
                doc.Delete(toDelete);
            }
        }

        private static void EnsureProjectParameterIdLote(Document doc)
        {
            EnsureProjectParameter(doc, ProjectParameterName);
        }

        private static void EnsureProjectParameter(Document doc, string parameterName)
        {
            if (ProjectParameterExists(doc, parameterName))
            {
                return;
            }

            Application app = doc.Application;
            string originalSpPath = app.SharedParametersFilename;
            string tempSpPath = Path.Combine(Path.GetTempPath(), "MYREVITPLUGIN_SHARED_PARAMS.txt");

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

                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(parameterName, SpecTypeId.String.Text);
                Definition definition = group.Definitions.get_Item(parameterName) ?? group.Definitions.Create(options);

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
                    throw new InvalidOperationException("No hay categorías válidas para aplicar el parámetro de loteo.");
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

        private class PolylineWithLayer
        {
            public PolyLine Polyline { get; set; }
            public string Layer { get; set; }
        }
    }
}
