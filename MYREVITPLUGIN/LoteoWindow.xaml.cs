using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MYREVITPLUGIN
{
    public class LoteoWindow : Window
    {
        private readonly UIApplication _uiapp;
        private readonly LoteoEngine _engine;
        private readonly ExternalEvent _externalEvent;
        private readonly LoteoExternalEventHandler _handler;

        // Paso 1
        private Label _lblDwgSeleccionado;
        private System.Windows.Controls.ComboBox _cmbCapaLimites;
        private System.Windows.Controls.ComboBox _cmbCapaTextos;

        // Paso 2
        private GroupBox _groupPaso2;
        private TextBlock _lblPaso2Info;
        private TextBlock _lblPaso2Resultado;

        // Paso 3
        private GroupBox _groupPaso3;
        private CheckBox _chkCrearParametro;
        private TextBlock _lblPaso3Resultado;

        // Error
        private TextBlock _lblError;

        public LoteoWindow(UIApplication uiapp, LoteoEngine engine, ExternalEvent externalEvent, LoteoExternalEventHandler handler)
        {
            _uiapp = uiapp;
            _engine = engine;
            _externalEvent = externalEvent;
            _handler = handler;
            _handler.SetWindow(this);

            BuildUI();
        }

        private void BuildUI()
        {
            Title = "M.I. Studio - Loteo";
            Width = 420;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x2E));
            Foreground = Brushes.White;
            FontFamily = new FontFamily("Segoe UI");
            SizeToContent = SizeToContent.Height;

            var root = new StackPanel { Margin = new Thickness(14) };

            // Título
            root.Children.Add(new TextBlock
            {
                Text = "Automatización de Loteo",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = Brushes.White
            });

            // ── PASO 1 ──────────────────────────────────────────────
            var paso1Panel = new StackPanel();

            var btnSeleccionar = MakeButton("Seleccionar DWG");
            btnSeleccionar.Click += BtnSeleccionarDwg_Click;
            paso1Panel.Children.Add(btnSeleccionar);

            _lblDwgSeleccionado = new Label
            {
                Content = "DWG: No seleccionado",
                Foreground = Brushes.White,
                Padding = new Thickness(0, 0, 0, 8)
            };
            paso1Panel.Children.Add(_lblDwgSeleccionado);

            paso1Panel.Children.Add(MakeLabel("Capa de límites (polylines):"));
            _cmbCapaLimites = MakeComboBox();
            paso1Panel.Children.Add(_cmbCapaLimites);

            paso1Panel.Children.Add(MakeLabel("Capa de textos (nombres):"));
            _cmbCapaTextos = MakeComboBox();
            paso1Panel.Children.Add(_cmbCapaTextos);

            var btnPaso1 = MakeButton("Ejecutar Paso 1");
            btnPaso1.Click += BtnPaso1_Click;
            paso1Panel.Children.Add(btnPaso1);

            root.Children.Add(MakeGroupBox("Paso 1 - Crear Lotes", paso1Panel));

            // ── PASO 2 ──────────────────────────────────────────────
            var paso2Panel = new StackPanel();

            _lblPaso2Info = new TextBlock
            {
                Text = "DirectShapes creados: 0",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            paso2Panel.Children.Add(_lblPaso2Info);

            var btnPaso2 = MakeButton("Ejecutar Paso 2");
            btnPaso2.Click += BtnPaso2_Click;
            paso2Panel.Children.Add(btnPaso2);

            _lblPaso2Resultado = new TextBlock
            {
                Text = "Resultado: -",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 8, 0, 0)
            };
            paso2Panel.Children.Add(_lblPaso2Resultado);

            _groupPaso2 = MakeGroupBox("Paso 2 - Nombrar Lotes", paso2Panel);
            _groupPaso2.IsEnabled = false;
            root.Children.Add(_groupPaso2);

            // ── PASO 3 ──────────────────────────────────────────────
            var paso3Panel = new StackPanel();

            _chkCrearParametro = new CheckBox
            {
                Content = "Crear parámetro ID-Lote si no existe",
                IsChecked = true,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            paso3Panel.Children.Add(_chkCrearParametro);

            var btnPaso3 = MakeButton("Ejecutar Paso 3");
            btnPaso3.Click += BtnPaso3_Click;
            paso3Panel.Children.Add(btnPaso3);

            _lblPaso3Resultado = new TextBlock
            {
                Text = "Resultado: -",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 8, 0, 0)
            };
            paso3Panel.Children.Add(_lblPaso3Resultado);

            _groupPaso3 = MakeGroupBox("Paso 3 - Asignar ID-Lote", paso3Panel);
            _groupPaso3.IsEnabled = false;
            root.Children.Add(_groupPaso3);

            // Error label
            _lblError = new TextBlock
            {
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x7B, 0x7B)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            };
            root.Children.Add(_lblError);

            Content = root;
        }

        // ── Event handlers ────────────────────────────────────────

        private void BtnSeleccionarDwg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ClearError();
                var uidoc = _uiapp.ActiveUIDocument;
                if (uidoc == null) { ShowError("No hay documento activo."); return; }

                Reference selectedRef = uidoc.Selection.PickObject(
                    Autodesk.Revit.UI.Selection.ObjectType.Element,
                    new DwgLinkSelectionFilter(uidoc.Document),
                    "Seleccione un DWG linkeado");

                ImportInstance instance = uidoc.Document.GetElement(selectedRef) as ImportInstance;
                if (instance == null) { ShowError("La selección no es un DWG válido."); return; }

                _engine.SetSelectedImport(instance.Id);

                List<string> layers = _engine.GetLayersFromImport(uidoc.Document, instance);
                _cmbCapaLimites.ItemsSource = layers;
                _cmbCapaTextos.ItemsSource = layers;
                if (layers.Count > 0) { _cmbCapaLimites.SelectedIndex = 0; _cmbCapaTextos.SelectedIndex = 0; }

                _lblDwgSeleccionado.Content = "DWG: " + (instance.Name ?? instance.Id.IntegerValue.ToString());
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
            catch (Exception ex) { ShowError("Error seleccionando DWG: " + ex.Message); }
        }

        private void BtnPaso1_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            if (_engine.SelectedImportId == ElementId.InvalidElementId) { ShowError("Primero seleccione un DWG."); return; }
            if (_cmbCapaLimites.SelectedItem == null || _cmbCapaTextos.SelectedItem == null) { ShowError("Seleccione capas."); return; }

            _handler.Request = LoteoRequestType.Step1CreateLots;
            _handler.LimitsLayer = _cmbCapaLimites.SelectedItem.ToString();
            _handler.TextLayer = _cmbCapaTextos.SelectedItem.ToString();
            _externalEvent.Raise();
        }

        private void BtnPaso2_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            _handler.Request = LoteoRequestType.Step2NameLots;
            _handler.TextLayer = _cmbCapaTextos.SelectedItem?.ToString();
            _externalEvent.Raise();
        }

        private void BtnPaso3_Click(object sender, RoutedEventArgs e)
        {
            ClearError();
            _handler.Request = LoteoRequestType.Step3AssignParameter;
            _handler.CreateParameterIfMissing = _chkCrearParametro.IsChecked == true;
            _externalEvent.Raise();
        }

        // ── Callbacks desde el handler ────────────────────────────

        public void OnStep1Completed(LoteoStepResult result)
        {
            if (!result.Success) { ShowError(result.Message); return; }
            _lblPaso2Info.Text = "DirectShapes creados: " + result.TotalCount;
            _groupPaso2.IsEnabled = true;
        }

        public void OnStep2Completed(LoteoStepResult result)
        {
            if (!result.Success) { ShowError(result.Message); return; }
            _lblPaso2Resultado.Text = "Resultado: " + result.NamedCount + " lotes nombrados de " + result.TotalCount;
            _groupPaso3.IsEnabled = true;
        }

        public void OnStep3Completed(LoteoStepResult result)
        {
            if (!result.Success) { ShowError(result.Message); return; }
            _lblPaso3Resultado.Text = "Resultado: " + result.UpdatedElements + " elementos actualizados";
        }

        // ── Helpers de UI ─────────────────────────────────────────

        private static Button MakeButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED))
            };
        }

        private static TextBlock MakeLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 3)
            };
        }

        private static System.Windows.Controls.ComboBox MakeComboBox()
        {
            return new System.Windows.Controls.ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        private static GroupBox MakeGroupBox(string header, UIElement content)
        {
            return new GroupBox
            {
                Header = header,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED)),
                Foreground = Brushes.White,
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 10),
                Content = content
            };
        }

        private void ShowError(string msg) => _lblError.Text = msg;
        private void ClearError() => _lblError.Text = string.Empty;
    }
}
