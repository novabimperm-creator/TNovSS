using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using SchemeBuilder.Core;
using TNovCommon;
using View = Autodesk.Revit.DB.View;

namespace SchemeBuilder.UI
{
    internal partial class WizardWindow : Window
    {
        private static readonly string[] StepNames =
        {
            "1. Оборудование",
            "2. Коды и наименования",
            "3. Зоны",
            "4. Оформление",
            "5. Схема",
            "6. Построение"
        };

        private readonly Document _document;
        private readonly SchemeSettings _settings;
        private readonly List<View> _legends;
        private readonly System.Windows.Controls.Grid[] _pages;

        private List<DeviceRow> _rows = new List<DeviceRow>();
        private SchemeData _zones = new SchemeData();
        private MatrixLayout _matrix = new MatrixLayout();
        private int _step;
        private bool _loading = true;

        public WizardWindow(Document document, SchemeSettings settings, List<View> legends)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _legends = legends ?? throw new ArgumentNullException(nameof(legends));

            InitializeComponent();

            _pages = new[] { Step0, Step1, Step2, Step3, Step4, Step5 };
            StepsList.ItemsSource = StepNames;

            DrawUgoBox.Content = UgoCaption();
            MatrixViewNameBox.Text = _settings.MatrixViewName;
            PanelPatternBox.Text = _settings.MatrixPanelPattern;
            SheetNumberBox.Text = _settings.SheetNumber;
            SheetNameBox.Text = _settings.SheetName;
            PlaceOnSheetBox.IsChecked = _settings.PlaceOnSheet;
            BuildLegendBox.IsChecked = _settings.BuildLegend;
            LineLegendBox.IsChecked = _settings.BuildLineLegend;
            BuildMatrixBox.IsChecked = _settings.BuildMatrix;
            CollapseTypicalBox.IsChecked = _settings.CollapseTypicalFloors;
            OrderReversedBox.IsChecked = _settings.MatrixOrderReversed;
            DrawUgoBox.IsChecked = _settings.MatrixDrawUgo;
            DrawTrunksBox.IsChecked = _settings.MatrixDrawTrunks;
            ShowUnzonedBox.IsChecked = _settings.MatrixShowUnzoned;

            FillCategories();
            FillZoning();
            FillLegends();
            FillTextTypes();
            FillTitleBlocks();
            LoadSizes();

            _loading = false;
            Rescan();
            ShowStep(0);
        }

        internal List<DeviceRow> Rows => _rows;

        internal View SelectedLegend => (LegendPicker.SelectedItem as LegendItem)?.View;

        internal MatrixLayout Matrix => _matrix;

        private List<CategoryItem> CategoryItems { get; } = new List<CategoryItem>();

        private ObservableCollection<CodeRowVm> CodeRows { get; } = new ObservableCollection<CodeRowVm>();

        private void ShowStep(int step)
        {
            if (step < 0 || step >= _pages.Length) return;

            _step = step;
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].Visibility = i == step ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }

            StepsList.SelectedIndex = step;
            BackButton.IsEnabled = step > 0;
            NextButton.Content = step == _pages.Length - 1 ? "Построить" : "Далее";

            switch (step)
            {
                case 2:
                    RecalculateZones();
                    break;
                case 3:
                    RefreshTemplateStatus();
                    break;
                case 4:
                    RefreshMatrix();
                    break;
                case 5:
                    RefreshFinalSummary();
                    break;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateStep()) return;

            if (_step < _pages.Length - 1)
            {
                ShowStep(_step + 1);
                return;
            }

            Collect();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private bool ValidateStep()
        {
            switch (_step)
            {
                case 0 when _rows.Count == 0:
                    Warn("В отмеченных категориях оборудования не нашлось. Отметьте другие категории.");
                    return false;

                case 1:
                    CommitCodesGrid();
                    if (_rows.All(r => !r.Include))
                    {
                        Warn("Не отмечено ни одной строки — строить будет нечего.");
                        return false;
                    }

                    return true;

                case 3 when IsChecked(BuildLegendBox) && SelectedLegend == null:
                    Warn("Не выбрана легенда.");
                    return false;

                case 4 when IsChecked(BuildMatrixBox) && _matrix.Bands.Count == 0:
                    Warn("Матрицу строить не из чего: на шаге 3 не нашлось ни одного помещения с оборудованием.");
                    return false;

                case 5 when !IsChecked(BuildLegendBox) && !IsChecked(BuildMatrixBox):
                    Warn("Ничего не отмечено к построению: включите легенду на шаге 4 или матрицу на шаге 5.");
                    return false;

                default:
                    return true;
            }
        }

        private static void Warn(string text)
        {
            new InfoWindow400(text).ShowDialog();
        }

        private void FillCategories()
        {
            Dictionary<BuiltInCategory, int> counts = DeviceScanner.CountByCategory(_document);
            CategoryItems.Clear();

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                int count = counts[category];
                if (count == 0) continue;

                CategoryItems.Add(new CategoryItem(category, count, _settings.Categories.Contains(category)));
            }

            if (!CategoryItems.Any(i => i.IsChecked) && CategoryItems.Count > 0) CategoryItems[0].IsChecked = true;

            CategoriesList.ItemsSource = CategoryItems;
        }

        private void OnCategoryChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            Rescan();
        }

        private List<BuiltInCategory> CheckedCategories()
        {
            return CategoryItems.Where(i => i.IsChecked).Select(i => i.Category).ToList();
        }

        private void Rescan()
        {
            _rows = DeviceScanner.Scan(_document, CheckedCategories(), _settings);

            int withCode = _rows.Count(r => !string.IsNullOrWhiteSpace(r.Code));
            FoundSummary.Text =
                "Найдено типов: " + _rows.Count.ToString(CultureInfo.CurrentCulture) +
                ", экземпляров: " + _rows.Sum(r => r.Count).ToString(CultureInfo.CurrentCulture) + ".\n" +
                "С буквенным кодом в марке: " + withCode.ToString(CultureInfo.CurrentCulture) +
                " — только они по умолчанию попадут в легенду.";

            FillGrid();
        }

        private void OnOnlyWithCodeChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            FillGrid();
        }

        private void FillGrid()
        {
            CommitCodesGrid();
            CodeRows.Clear();

            bool onlyWithCode = IsChecked(OnlyWithCode);
            foreach (DeviceRow row in _rows)
            {
                if (onlyWithCode && string.IsNullOrWhiteSpace(row.Code)) continue;
                CodeRows.Add(new CodeRowVm(row, RefreshRowsSummary));
            }

            CodesGrid.ItemsSource = CodeRows;
            RefreshRowsSummary();
        }

        private void RefreshRowsSummary()
        {
            int checkedRows = CodeRows.Count(r => r.Include);
            int devices = CodeRows.Where(r => r.Include).Sum(r => r.Count);

            RowsSummary.Text = "Строк в таблице: " + CodeRows.Count.ToString(CultureInfo.CurrentCulture) +
                               ", отмечено: " + checkedRows.ToString(CultureInfo.CurrentCulture) +
                               ", устройств в них: " + devices.ToString(CultureInfo.CurrentCulture);
        }

        private void CommitCodesGrid()
        {
            CodesGrid.CommitEdit();
            CodesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }

        private void FillZoning()
        {
            ZoningBox.Items.Add(new ZoningItem(ZoneSource.ApartmentOrRoom, "Квартире, иначе помещению"));
            ZoningBox.Items.Add(new ZoningItem(ZoneSource.RoomName, "Имени помещения"));
            ZoningBox.Items.Add(new ZoningItem(ZoneSource.RoomNumber, "Номеру помещения"));
            ZoningBox.Items.Add(new ZoningItem(ZoneSource.RoomParameter, "Параметру помещения"));
            ZoningBox.Items.Add(new ZoningItem(ZoneSource.DeviceParameter, "Параметру устройства"));

            ZoningBox.SelectedIndex = ZoningBox.Items.Cast<ZoningItem>().ToList()
                .FindIndex(i => i.Source == _settings.Zoning);

            if (ZoningBox.SelectedIndex < 0) ZoningBox.SelectedIndex = 0;
            ApplyZoningFields();
        }

        private ZoneSource SelectedZoning => ((ZoningItem)ZoningBox.SelectedItem).Source;

        private void OnZoningChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ZoningBox.SelectedItem == null) return;
            ApplyZoningFields();
        }

        private void ApplyZoningFields()
        {
            ZoneSource zoning = SelectedZoning;

            switch (zoning)
            {
                case ZoneSource.ApartmentOrRoom:
                    ZoneParameterLabel.Text = "Параметр помещения с номером квартиры:";
                    ZoneParameterBox.IsEnabled = true;
                    ZoneParameterBox.Text = _settings.ApartmentParameterName;
                    break;

                case ZoneSource.RoomParameter:
                case ZoneSource.DeviceParameter:
                    ZoneParameterLabel.Text = "Имя параметра:";
                    ZoneParameterBox.IsEnabled = true;
                    ZoneParameterBox.Text = _settings.ZoneParameterName;
                    break;

                default:
                    ZoneParameterLabel.Text = "Имя параметра:";
                    ZoneParameterBox.IsEnabled = false;
                    break;
            }
        }

        private void ReadZoneParameter()
        {
            string value = ZoneParameterBox.Text.Trim();

            if (SelectedZoning == ZoneSource.ApartmentOrRoom)
            {
                _settings.ApartmentParameterName = value;
                return;
            }

            if (ZoneParameterBox.IsEnabled) _settings.ZoneParameterName = value;
        }

        private void RecalculateZones_Click(object sender, RoutedEventArgs e) => RecalculateZones();

        private void RecalculateZones()
        {
            _settings.Zoning = SelectedZoning;
            ReadZoneParameter();

            _zones = SchemeScanner.Scan(_document, _settings, _rows);

            var rows = new List<ZoneRowVm>();
            foreach (FloorGroup floor in _zones.Floors)
            {
                string elevation = Millimeters(floor.Elevation);
                foreach (ZoneGroup zone in floor.Zones)
                {
                    rows.Add(new ZoneRowVm
                    {
                        Floor = floor.Name,
                        Elevation = elevation,
                        Zone = zone.Name,
                        Count = zone.Total,
                        Missing = zone.Name == SchemeScanner.NoZone
                    });
                }
            }

            ZonesGrid.ItemsSource = rows;

            ZonesSummary.Text = "Этажей: " + _zones.Floors.Count.ToString(CultureInfo.CurrentCulture) +
                                ", зон: " + _zones.Floors.Sum(f => f.Zones.Count).ToString(CultureInfo.CurrentCulture) +
                                ", устройств: " + _zones.Placed.ToString(CultureInfo.CurrentCulture) +
                                (_zones.WithoutZone > 0
                                    ? ", без зоны: " + _zones.WithoutZone.ToString(CultureInfo.CurrentCulture)
                                    : string.Empty);

            var notes = new StringBuilder(string.Join("   ", _zones.Notes));
            if (_zones.WithoutZone > 0 && _zones.WithoutZone == _zones.Placed)
            {
                notes.AppendLine();
                notes.Append("Ни одно устройство не попало в зону. Если модель отсоединена от связей, " +
                             "помещений в ней нет — возьмите зону из параметра устройства.");
            }

            ZonesNotes.Text = notes.ToString();
        }

        private void OnBuildLegendChanged(object sender, RoutedEventArgs e)
        {
            if (LegendPanel == null) return;
            LegendPanel.IsEnabled = IsChecked(BuildLegendBox);
        }

        private void FillLegends()
        {
            foreach (View legend in _legends) LegendPicker.Items.Add(new LegendItem(legend));

            if (_legends.Count == 0)
            {
                BuildLegendBox.IsChecked = false;
                BuildLegendBox.IsEnabled = false;
                BuildLegendBox.Content = "Строить легенду УГО — в проекте нет ни одной легенды";
                return;
            }

            int saved = _legends.FindIndex(v => v.Id == _settings.LegendViewId);
            LegendPicker.SelectedIndex = saved >= 0 ? saved : 0;
        }

        private void OnLegendPicked(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            RefreshTemplateStatus();
        }

        private void RefreshTemplateStatus()
        {
            View legend = SelectedLegend;
            if (legend == null)
            {
                TemplateStatus.Text = string.Empty;
                return;
            }

            LegendBuilder.Template template = LegendBuilder.FindTemplate(_document, legend);
            string scale = " Масштаб вида 1:" + legend.Scale.ToString(CultureInfo.CurrentCulture) + ".";

            if (template == null)
            {
                TemplateStatus.Foreground = (Brush)FindResource("MutedBrush");
                TemplateStatus.Text =
                    "В проекте нет ни одного компонента легенды — колонка УГО останется пустой.\n" +
                    "Символ рисует штатный «Компонент легенды», а создать его из кода Revit не даёт. Вставьте " +
                    "в любую легенду один компонент (Аннотации → Компонент легенды) и вернитесь сюда." + scale;
                return;
            }

            TemplateStatus.Foreground = (Brush)FindResource("AccentBrush");
            TemplateStatus.Text = template.Source.Id == legend.Id
                ? "Компонент-образец найден в этой легенде: символы будут поставлены." + scale
                : "Образец взят из легенды «" + template.Source.Name + "» — символы будут поставлены." + scale;
        }

        private void FillTextTypes()
        {
            ElementId fallback = _document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            foreach (TextNoteType type in new FilteredElementCollector(_document)
                         .OfClass(typeof(TextNoteType))
                         .Cast<TextNoteType>()
                         .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                TextTypeBox.Items.Add(new TextTypeItem(type));
            }

            List<TextTypeItem> items = TextTypeBox.Items.Cast<TextTypeItem>().ToList();
            int saved = items.FindIndex(i => i.Id == _settings.TextTypeId);
            if (saved < 0) saved = items.FindIndex(i => i.Id == fallback);
            if (saved < 0 && items.Count > 0) saved = 0;
            if (saved >= 0) TextTypeBox.SelectedIndex = saved;
        }

        private void LoadSizes()
        {
            RowHeightBox.Text = Clamp(_settings.RowHeightMm, 3, 40).ToString(CultureInfo.CurrentCulture);
            SymbolWidthBox.Text = Clamp(_settings.SymbolWidthMm, 5, 60).ToString(CultureInfo.CurrentCulture);
            CodeWidthBox.Text = Clamp(_settings.CodeWidthMm, 5, 60).ToString(CultureInfo.CurrentCulture);
            DescriptionWidthBox.Text = Clamp(_settings.DescriptionWidthMm, 20, 300).ToString(CultureInfo.CurrentCulture);
            RowsPerColumnBox.Text = Clamp(_settings.RowsPerColumn, 1, 200).ToString(CultureInfo.CurrentCulture);

            MatrixScaleBox.Text = Clamp(_settings.MatrixScale, 1, 500).ToString(CultureInfo.CurrentCulture);
            MatrixBlockBox.Text = Clamp(_settings.MatrixBlockMm, 5, 60).ToString(CultureInfo.CurrentCulture);
            MatrixBlockHeightBox.Text = Clamp(_settings.MatrixBlockHeightMm, 5, 60).ToString(CultureInfo.CurrentCulture);
            MatrixPerLineBox.Text = Clamp(_settings.MatrixBlocksPerLine, 1, 40).ToString(CultureInfo.CurrentCulture);
            MatrixFloorColumnBox.Text = Clamp(_settings.MatrixFloorColumnMm, 6, 60).ToString(CultureInfo.CurrentCulture);

            CellOrderBox.Items.Add(new OrderItem(ZoneOrder.AlongX, "по X — дом вдоль X"));
            CellOrderBox.Items.Add(new OrderItem(ZoneOrder.AlongY, "по Y — дом поперёк"));
            CellOrderBox.Items.Add(new OrderItem(ZoneOrder.SideThenX, "стороны, затем X"));
            CellOrderBox.Items.Add(new OrderItem(ZoneOrder.SideThenY, "стороны, затем Y"));
            CellOrderBox.Items.Add(new OrderItem(ZoneOrder.ByName, "по имени"));
            CellOrderBox.SelectedIndex = Math.Max(0, CellOrderBox.Items.Cast<OrderItem>().ToList()
                .FindIndex(i => i.Order == _settings.MatrixOrder));

            TrunkPositionBox.Items.Add(new TrunkItem(TrunkPosition.Middle, "посередине"));
            TrunkPositionBox.Items.Add(new TrunkItem(TrunkPosition.Left, "слева"));
            TrunkPositionBox.Items.Add(new TrunkItem(TrunkPosition.Right, "справа"));
            TrunkPositionBox.SelectedIndex = Math.Max(0, TrunkPositionBox.Items.Cast<TrunkItem>().ToList()
                .FindIndex(i => i.Position == _settings.MatrixTrunkPosition));

            LegendPanel.IsEnabled = IsChecked(BuildLegendBox);
            MatrixPanel.IsEnabled = IsChecked(BuildMatrixBox);
            MatrixGrid.IsEnabled = IsChecked(BuildMatrixBox);
        }

        private void FillTitleBlocks()
        {
            foreach (FamilySymbol block in SheetBuilder.TitleBlocks(_document))
            {
                TitleBlockBox.Items.Add(SheetBuilder.Caption(block));
            }

            int saved = string.IsNullOrWhiteSpace(_settings.SheetTitleBlock)
                ? -1
                : TitleBlockBox.Items.Cast<string>().ToList()
                    .FindIndex(i => i.IndexOf(_settings.SheetTitleBlock, StringComparison.CurrentCultureIgnoreCase) >= 0);

            if (saved < 0 && TitleBlockBox.Items.Count > 0) saved = 0;
            if (saved >= 0) TitleBlockBox.SelectedIndex = saved;
        }

        private void OnBuildMatrixChanged(object sender, RoutedEventArgs e)
        {
            if (MatrixPanel == null) return;
            bool on = IsChecked(BuildMatrixBox);
            MatrixPanel.IsEnabled = on;
            MatrixGrid.IsEnabled = on;
        }

        private void RefreshMatrix_Click(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            RefreshMatrix();
        }

        private void RefreshMatrix_Click(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            RefreshMatrix();
        }

        private void RefreshMatrix_LostFocus(object sender, RoutedEventArgs e) => RefreshMatrix();

        private void RefreshMatrixSummary_LostFocus(object sender, RoutedEventArgs e) => RefreshMatrixSummary();

        private TrunkPosition SelectedTrunkPosition =>
            (TrunkPositionBox.SelectedItem as TrunkItem)?.Position ?? TrunkPosition.Middle;

        private ZoneOrder SelectedOrder => (CellOrderBox.SelectedItem as OrderItem)?.Order ?? ZoneOrder.AlongX;

        private string UgoCaption()
        {
            return _settings.UgoCount > 0
                ? "Ставить УГО в блоки (сопоставлено семейств: " +
                  _settings.UgoCount.ToString(CultureInfo.CurrentCulture) + ")"
                : "Ставить УГО в блоки — сопоставления нет, сделайте его кнопкой «УГО» на ленте";
        }

        private void RefreshMatrix()
        {
            _matrix = MatrixLayout.Build(_zones, MatrixSizes(), IsChecked(CollapseTypicalBox));

            MatrixGrid.Columns.Clear();
            MatrixGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Этаж",
                Binding = new System.Windows.Data.Binding("Caption"),
                Width = 170,
                IsReadOnly = true,
                ElementStyle = (Style)FindResource("DarkDataGridTextElementStyle")
            });

            int widest = _matrix.Bands.Count == 0 ? 0 : _matrix.Bands.Max(b => b.Cells.Count);
            for (int index = 0; index < widest; index++)
            {
                MatrixGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = (index + 1).ToString(CultureInfo.CurrentCulture),
                    Binding = new System.Windows.Data.Binding("Cells[" + index + "]"),
                    Width = 170,
                    IsReadOnly = true,
                    ElementStyle = (Style)FindResource("DarkDataGridTextElementStyle")
                });
            }

            var rows = new List<MatrixRowVm>();
            foreach (MatrixBand band in _matrix.Bands)
            {
                var cells = new string[Math.Max(widest, 1)];
                for (int i = 0; i < band.Cells.Count; i++)
                {
                    MatrixCell cell = band.Cells[i];
                    cells[i] = cell.Title + " — " +
                               string.Join("; ", cell.Blocks.Select(b => b.Caption + " " + b.Amount));
                }

                rows.Add(new MatrixRowVm
                {
                    Caption = band.Caption,
                    IsTypical = band.IsTypical,
                    Cells = cells
                });
            }

            MatrixGrid.ItemsSource = rows;
            RefreshMatrixSummary();
            OnBuildMatrixChanged(null, null);
        }

        private void MatrixGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            if (e.Row.Item is MatrixRowVm row && row.IsTypical)
            {
                e.Row.Background = (Brush)FindResource("SelectedHoverBrush");
            }
        }

        private void RefreshMatrixSummary()
        {
            SchemeSettings preview = MatrixSizes();

            var text = new StringBuilder();
            text.Append("Строк: " + _matrix.Bands.Count.ToString(CultureInfo.CurrentCulture) +
                        ", ячеек: " + _matrix.Cells.ToString(CultureInfo.CurrentCulture) +
                        ", устройств: " + _matrix.Devices.ToString(CultureInfo.CurrentCulture) +
                        ", на листе: " + MatrixBuilder.SizeCaption(_matrix, preview));

            double width = MatrixBuilder.WidthMm(_matrix, preview);
            if (width > 1189)
            {
                text.Append(" — шире листа A0. Уменьшите блок или разбейте схему по секциям.");
            }

            foreach (string note in _matrix.Notes) text.AppendLine().Append(note);

            MatrixSummary.Text = text.ToString();
            MatrixSummary.Foreground = width > 1189
                ? (Brush)FindResource("MutedBrush")
                : (Brush)FindResource("PrimaryTextBrush");
        }

        private SchemeSettings MatrixSizes()
        {
            return new SchemeSettings
            {
                MatrixBlockMm = ReadDouble(MatrixBlockBox, 5, 60, 12),
                MatrixBlockHeightMm = ReadDouble(MatrixBlockHeightBox, 5, 60, 30),
                MatrixBlocksPerLine = ReadInt(MatrixPerLineBox, 1, 40, 12),
                MatrixFloorColumnMm = ReadDouble(MatrixFloorColumnBox, 6, 60, 12),
                MatrixShowUnzoned = IsChecked(ShowUnzonedBox),
                MatrixTrunkPosition = SelectedTrunkPosition,
                MatrixDrawTrunks = IsChecked(DrawTrunksBox),
                MatrixOrder = SelectedOrder,
                MatrixOrderReversed = IsChecked(OrderReversedBox),
                MatrixScale = ReadInt(MatrixScaleBox, 1, 500, 10)
            };
        }

        private void OnPlaceOnSheetChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            RefreshFinalSummary();
        }

        private void RefreshFinalSummary()
        {
            View legend = SelectedLegend;
            bool hasTemplate = legend != null && LegendBuilder.FindTemplateComponent(_document, legend) != null;
            int included = _rows.Count(r => r.Include);

            var text = new StringBuilder();

            if (IsChecked(BuildLegendBox))
            {
                text.AppendLine("Легенда УГО");
                text.AppendLine("    строк: " + included.ToString(CultureInfo.CurrentCulture));
                text.AppendLine("    вид: " + (legend?.Name ?? "не выбран"));
                text.AppendLine("    символы: " + (hasTemplate ? "будут поставлены" : "не будут — нет компонента-образца"));
            }
            else
            {
                text.AppendLine("Легенда УГО — не строится (снята галка на шаге 4).");
            }

            text.AppendLine();

            if (IsChecked(BuildMatrixBox))
            {
                text.AppendLine("Матрица схемы");
                text.AppendLine("    строк: " + _matrix.Bands.Count.ToString(CultureInfo.CurrentCulture) +
                                " (типовых: " + _matrix.Bands.Count(b => b.IsTypical).ToString(CultureInfo.CurrentCulture) + ")");
                text.AppendLine("    ячеек: " + _matrix.Cells.ToString(CultureInfo.CurrentCulture) +
                                ", устройств: " + _matrix.Devices.ToString(CultureInfo.CurrentCulture));
                text.AppendLine("    вид: " + MatrixViewNameBox.Text.Trim() + ", 1:" +
                                ReadInt(MatrixScaleBox, 1, 500, 10).ToString(CultureInfo.CurrentCulture));
                text.AppendLine("    на листе: " + MatrixBuilder.SizeCaption(_matrix, MatrixSizes()));

                if (IsChecked(DrawUgoBox) && _settings.UgoCount == 0)
                {
                    text.AppendLine("    УГО: сопоставления нет — все блоки будут текстовыми. " +
                                    "Сделайте сопоставление кнопкой «УГО» на ленте и постройте заново.");
                }
            }
            else
            {
                text.AppendLine("Матрица схемы — не чертится (снята галка на шаге 5).");
            }

            text.AppendLine();
            if (IsChecked(BuildMatrixBox) && IsChecked(PlaceOnSheetBox))
            {
                text.AppendLine("Лист " + SheetNumberBox.Text.Trim() + " «" + SheetNameBox.Text.Trim() + "» — схема ляжет на него.");
                text.AppendLine();
            }

            text.AppendLine("Нарисованное плагином в прошлый раз будет снесено и построено заново.");
            text.AppendLine("Добавленное руками не трогается.");

            if (_zones.WithoutZone > 0)
            {
                text.AppendLine();
                text.AppendLine("Устройств без зоны: " + _zones.WithoutZone.ToString(CultureInfo.CurrentCulture) +
                                " — на схеме им место только в колонке «" + SchemeScanner.NoZone +
                                "», легенде это не мешает.");
            }

            FinalSummary.Text = text.ToString();
        }

        private void Collect()
        {
            CommitCodesGrid();

            foreach (DeviceRow row in _rows) _settings.Remember(row, row.ModelCode, row.ModelDescription);

            _settings.Categories.Clear();
            foreach (BuiltInCategory category in CheckedCategories()) _settings.Categories.Add(category);

            _settings.LegendViewId = SelectedLegend?.Id ?? ElementId.InvalidElementId;
            _settings.Zoning = SelectedZoning;
            ReadZoneParameter();
            _settings.TextTypeId = (TextTypeBox.SelectedItem as TextTypeItem)?.Id ?? ElementId.InvalidElementId;
            _settings.RowHeightMm = ReadDouble(RowHeightBox, 3, 40, 8);
            _settings.SymbolWidthMm = ReadDouble(SymbolWidthBox, 5, 60, 14);
            _settings.CodeWidthMm = ReadDouble(CodeWidthBox, 5, 60, 16);
            _settings.DescriptionWidthMm = ReadDouble(DescriptionWidthBox, 20, 300, 95);
            _settings.RowsPerColumn = ReadInt(RowsPerColumnBox, 1, 200, 22);

            _settings.BuildLegend = IsChecked(BuildLegendBox);
            _settings.BuildLineLegend = IsChecked(LineLegendBox);
            _settings.BuildMatrix = IsChecked(BuildMatrixBox);
            _settings.CollapseTypicalFloors = IsChecked(CollapseTypicalBox);
            _settings.MatrixViewName = MatrixViewNameBox.Text.Trim();
            _settings.MatrixScale = ReadInt(MatrixScaleBox, 1, 500, 10);
            _settings.MatrixBlockMm = ReadDouble(MatrixBlockBox, 5, 60, 12);
            _settings.MatrixBlockHeightMm = ReadDouble(MatrixBlockHeightBox, 5, 60, 30);
            _settings.MatrixBlocksPerLine = ReadInt(MatrixPerLineBox, 1, 40, 12);
            _settings.MatrixFloorColumnMm = ReadDouble(MatrixFloorColumnBox, 6, 60, 12);
            _settings.MatrixOrder = SelectedOrder;
            _settings.MatrixOrderReversed = IsChecked(OrderReversedBox);
            _settings.MatrixDrawUgo = IsChecked(DrawUgoBox);
            _settings.MatrixDrawTrunks = IsChecked(DrawTrunksBox);
            _settings.MatrixShowUnzoned = IsChecked(ShowUnzonedBox);
            _settings.MatrixTrunkPosition = SelectedTrunkPosition;
            _settings.MatrixPanelPattern = PanelPatternBox.Text.Trim();
            _settings.PlaceOnSheet = IsChecked(PlaceOnSheetBox);
            _settings.SheetNumber = SheetNumberBox.Text.Trim();
            _settings.SheetName = SheetNameBox.Text.Trim();
            _settings.SheetTitleBlock = TitleBlockBox.SelectedItem as string ?? string.Empty;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpLinks.ShowHelp("-");
        }

        private static bool IsChecked(System.Windows.Controls.CheckBox box) => box.IsChecked == true;

        private static int ReadInt(TextBox box, int min, int max, int fallback)
        {
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value))
            {
                return fallback;
            }

            return Math.Min(max, Math.Max(min, value));
        }

        private static double ReadDouble(TextBox box, double min, double max, double fallback)
        {
            if (!double.TryParse(box.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double value))
            {
                return fallback;
            }

            return Math.Min(max, Math.Max(min, value));
        }

        private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));

        private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));

        private static string Millimeters(double feet)
        {
            double millimeters = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
            return millimeters.ToString("+#,##0;-#,##0;0", CultureInfo.CurrentCulture);
        }

        private sealed class CategoryItem : ObservableObject
        {
            private bool _isChecked;

            public CategoryItem(BuiltInCategory category, int count, bool isChecked)
            {
                Category = category;
                _isChecked = isChecked;

                string name;
                try
                {
                    name = LabelUtils.GetLabelFor(category);
                }
                catch (Exception)
                {
                    name = category.ToString();
                }

                Caption = name + "  —  " + count.ToString(CultureInfo.CurrentCulture);
            }

            public BuiltInCategory Category { get; }

            public string Caption { get; }

            public bool IsChecked
            {
                get => _isChecked;
                set => SetProperty(ref _isChecked, value);
            }
        }

        private sealed class LegendItem
        {
            public LegendItem(View view) => View = view;

            public View View { get; }

            public override string ToString() => View.Name + "   (1:" + View.Scale.ToString(CultureInfo.CurrentCulture) + ")";
        }

        private sealed class TextTypeItem
        {
            private readonly string _caption;

            public TextTypeItem(TextNoteType type)
            {
                Id = type.Id;
                double size = type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0.0;
                double millimeters = UnitUtils.ConvertFromInternalUnits(size, UnitTypeId.Millimeters);
                _caption = type.Name + "   " + millimeters.ToString("0.#", CultureInfo.CurrentCulture) + " мм";
            }

            public ElementId Id { get; }

            public override string ToString() => _caption;
        }

        private sealed class TrunkItem
        {
            public TrunkItem(TrunkPosition position, string caption)
            {
                Position = position;
                Caption = caption;
            }

            public TrunkPosition Position { get; }

            public string Caption { get; }

            public override string ToString() => Caption;
        }

        private sealed class OrderItem
        {
            public OrderItem(ZoneOrder order, string caption)
            {
                Order = order;
                Caption = caption;
            }

            public ZoneOrder Order { get; }

            public string Caption { get; }

            public override string ToString() => Caption;
        }

        private sealed class ZoningItem
        {
            public ZoningItem(ZoneSource source, string caption)
            {
                Source = source;
                Caption = caption;
            }

            public ZoneSource Source { get; }

            private string Caption { get; }

            public override string ToString() => Caption;
        }
    }

    internal sealed class CodeRowVm : ObservableObject
    {
        private readonly Action _onChanged;

        public CodeRowVm(DeviceRow row, Action onChanged)
        {
            Row = row;
            _onChanged = onChanged;
        }

        public DeviceRow Row { get; }

        public bool Include
        {
            get => Row.Include;
            set
            {
                Row.Include = value;
                OnPropertyChanged();
                _onChanged();
            }
        }

        public string Code
        {
            get => Row.Code;
            set
            {
                Row.Code = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Problem));
            }
        }

        public string Description
        {
            get => Row.Description;
            set
            {
                Row.Description = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Problem));
            }
        }

        public string TypeCaption => Row.ToString();

        public int Count => Row.Count;

        public string Problem => Row.Problem;
    }

    internal sealed class ZoneRowVm
    {
        public string Floor { get; set; }

        public string Elevation { get; set; }

        public string Zone { get; set; }

        public int Count { get; set; }

        public bool Missing { get; set; }
    }

    internal sealed class MatrixRowVm
    {
        public string Caption { get; set; }

        public bool IsTypical { get; set; }

        public string[] Cells { get; set; }
    }
}
