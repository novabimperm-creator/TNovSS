using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using SchemeBuilder.Core;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;
using Point = System.Drawing.Point;
using View = Autodesk.Revit.DB.View;

namespace SchemeBuilder.UI
{
    /// <summary>
    /// Конструктор раздела СС: один вход и пять шагов вместо россыпи кнопок.
    ///
    /// Порядок шагов повторяет порядок решений, которые всё равно приходится принимать:
    /// что считать оборудованием → как оно называется → чем считать зону → как оформить →
    /// построить. Каждый шаг показывает результат предыдущего, поэтому ошибку видно сразу,
    /// а не на готовом листе.
    /// </summary>
    internal class WizardForm : Form
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

        private readonly ListBox _steps = new ListBox();
        private readonly Panel _content = new Panel();
        private readonly Panel[] _pages = new Panel[StepNames.Length];
        private readonly Button _back = new Button();
        private readonly Button _next = new Button();
        private readonly Label _hint = new Label();

        // Шаг 1
        private readonly CheckedListBox _categories = new CheckedListBox();
        private readonly Label _foundSummary = new Label();

        // Шаг 2
        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _rowsSummary = new Label();
        private readonly CheckBox _onlyWithCode = new CheckBox();

        // Шаг 3
        private readonly ComboBox _zoning = new ComboBox();
        private readonly TextBox _zoneParameter = new TextBox();
        private readonly Label _zoneParameterLabel = new Label();
        private readonly DataGridView _zonesGrid = new DataGridView();
        private readonly Label _zonesSummary = new Label();
        private readonly Label _zonesNotes = new Label();

        // Шаг 4
        private readonly CheckBox _buildLegend = new CheckBox();
        private readonly CheckBox _lineLegend = new CheckBox();
        private readonly ComboBox _legendPicker = new ComboBox();
        private readonly ComboBox _textType = new ComboBox();
        private readonly Label _templateStatus = new Label();
        private readonly NumericUpDown _rowHeight = Spin(3, 40);
        private readonly NumericUpDown _symbolWidth = Spin(5, 60);
        private readonly NumericUpDown _codeWidth = Spin(5, 60);
        private readonly NumericUpDown _descriptionWidth = Spin(20, 300);
        private readonly NumericUpDown _rowsPerColumn = Spin(1, 200);
        private readonly List<Control> _legendControls = new List<Control>();

        // Шаг 5
        private readonly CheckBox _buildMatrix = new CheckBox();
        private readonly CheckBox _collapseTypical = new CheckBox();
        private readonly ComboBox _cellOrder = new ComboBox();
        private readonly CheckBox _orderReversed = new CheckBox();
        private readonly CheckBox _drawUgo = new CheckBox();
        private readonly CheckBox _drawTrunks = new CheckBox();
        private readonly CheckBox _showUnzoned = new CheckBox();
        private readonly ComboBox _trunkPosition = new ComboBox();
        private readonly TextBox _panelPattern = new TextBox();
        private readonly TextBox _matrixViewName = new TextBox();
        private readonly NumericUpDown _matrixScale = Spin(1, 500);
        private readonly NumericUpDown _matrixBlock = Spin(5, 60);
        private readonly NumericUpDown _matrixBlockHeight = Spin(5, 60);
        private readonly NumericUpDown _matrixPerLine = Spin(1, 40);
        private readonly NumericUpDown _matrixFloorColumn = Spin(6, 60);
        private readonly DataGridView _matrixGrid = new DataGridView();
        private readonly Label _matrixSummary = new Label();
        private readonly List<Control> _matrixControls = new List<Control>();

        // Шаг 6
        private readonly CheckBox _placeOnSheet = new CheckBox();
        private readonly TextBox _sheetNumber = new TextBox();
        private readonly TextBox _sheetName = new TextBox();
        private readonly ComboBox _titleBlock = new ComboBox();
        private readonly Label _finalSummary = new Label();

        private List<DeviceRow> _rows = new List<DeviceRow>();
        private SchemeData _zones = new SchemeData();
        private MatrixLayout _matrix = new MatrixLayout();
        private int _step;

        public WizardForm(Document document, SchemeSettings settings, List<View> legends)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _legends = legends ?? throw new ArgumentNullException(nameof(legends));

            Text = "Структурная схема — конструктор";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 660);
            Size = new Size(1080, 720);
            Font = SystemFonts.MessageBoxFont;

            BuildLayout();

            FillCategories();
            _categories.ItemCheck += OnCategoryChecked;

            FillZoning();
            FillLegends();
            FillTextTypes();
            LoadSizes();

            Rescan();
            ShowStep(0);
        }

        public List<DeviceRow> Rows => _rows;

        public View SelectedLegend => (_legendPicker.SelectedItem as LegendItem)?.View;

        /// <summary>Раскладка матрицы, показанная на шаге 5. Строит по ней команда, а не мастер.</summary>
        public MatrixLayout Matrix => _matrix;

        // ===== Каркас =====

        private void BuildLayout()
        {
            _steps.Dock = DockStyle.Left;
            _steps.Width = 210;
            _steps.IntegralHeight = false;
            _steps.SelectionMode = SelectionMode.One;
            _steps.Items.AddRange(StepNames.Cast<object>().ToArray());
            // Список шагов — указатель, а не навигация: переход только кнопками, иначе можно
            // прыгнуть на построение, не пройдя сбор данных.
            _steps.Enabled = false;

            _content.Dock = DockStyle.Fill;
            _content.Padding = new Padding(14, 10, 14, 6);

            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i] = new Panel { Dock = DockStyle.Fill, Visible = false };
                _content.Controls.Add(_pages[i]);
            }

            BuildStepEquipment(_pages[0]);
            BuildStepCodes(_pages[1]);
            BuildStepZones(_pages[2]);
            BuildStepLayout(_pages[3]);
            BuildStepMatrix(_pages[4]);
            BuildStepFinish(_pages[5]);

            _hint.Dock = DockStyle.Bottom;
            _hint.Height = 22;
            _hint.Padding = new Padding(16, 3, 12, 0);
            _hint.ForeColor = SystemColors.GrayText;

            _back.Text = "Назад";
            _back.Size = new Size(110, 28);
            _back.Click += (s, e) => ShowStep(_step - 1);

            _next.Text = "Далее";
            _next.Size = new Size(130, 28);
            _next.Click += OnNext;

            var cancel = new Button { Text = "Отмена", Size = new Size(110, 28), DialogResult = DialogResult.Cancel };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(8, 9, 8, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(_next);
            buttons.Controls.Add(_back);

            Controls.Add(_content);
            Controls.Add(_steps);
            Controls.Add(_hint);
            Controls.Add(buttons);

            CancelButton = cancel;
        }

        private void ShowStep(int step)
        {
            if (step < 0 || step >= _pages.Length) return;

            _step = step;
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == step;

            _steps.SelectedIndex = step;
            _back.Enabled = step > 0;
            _next.Text = step == _pages.Length - 1 ? "Построить" : "Далее";

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

        private void OnNext(object sender, EventArgs e)
        {
            if (!ValidateStep()) return;

            if (_step < _pages.Length - 1)
            {
                ShowStep(_step + 1);
                return;
            }

            Collect();
            DialogResult = DialogResult.OK;
        }

        private bool ValidateStep()
        {
            switch (_step)
            {
                case 0 when _rows.Count == 0:
                    Warn("В отмеченных категориях оборудования не нашлось. Отметьте другие категории.");
                    return false;

                case 1:
                    ReadGrid();
                    if (_rows.All(r => !r.Include))
                    {
                        Warn("Не отмечено ни одной строки — строить будет нечего.");
                        return false;
                    }

                    return true;

                case 3 when _buildLegend.Checked && SelectedLegend == null:
                    Warn("Не выбрана легенда.");
                    return false;

                case 4 when _buildMatrix.Checked && _matrix.Bands.Count == 0:
                    Warn("Матрицу строить не из чего: на шаге 3 не нашлось ни одного помещения с оборудованием.");
                    return false;

                case 5 when !_buildLegend.Checked && !_buildMatrix.Checked:
                    Warn("Ничего не отмечено к построению: включите легенду на шаге 4 или матрицу на шаге 5.");
                    return false;

                default:
                    return true;
            }
        }

        private void Warn(string text)
        {
            MessageBox.Show(this, text, "Конструктор", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ===== Шаг 1: оборудование =====

        private void BuildStepEquipment(Control page)
        {
            page.Controls.Add(Caption("Шаг 1. Что считать оборудованием раздела", 0));

            var hint = new Label
            {
                Text = "В списке только те категории, где в модели что-то есть, с числом экземпляров. " +
                       "Отверстия, гильзы и закладные обычно лежат в обобщённых моделях — их включать не нужно.",
                Location = new Point(0, 28),
                Size = new Size(800, 34),
                ForeColor = SystemColors.GrayText
            };

            _categories.Location = new Point(0, 68);
            _categories.Size = new Size(430, 190);
            _categories.CheckOnClick = true;
            _categories.IntegralHeight = false;

            _foundSummary.Location = new Point(0, 272);
            _foundSummary.Size = new Size(800, 40);

            page.Controls.Add(hint);
            page.Controls.Add(_categories);
            page.Controls.Add(_foundSummary);
        }

        private void FillCategories()
        {
            Dictionary<BuiltInCategory, int> counts = DeviceScanner.CountByCategory(_document);

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                int count = counts[category];
                if (count == 0) continue;

                _categories.Items.Add(new CategoryItem(category, count), _settings.Categories.Contains(category));
            }

            if (_categories.CheckedItems.Count == 0 && _categories.Items.Count > 0) _categories.SetItemChecked(0, true);
        }

        private void OnCategoryChecked(object sender, ItemCheckEventArgs e)
        {
            if (!IsHandleCreated) return;

            BeginInvoke(new Action(Rescan));
        }

        private List<BuiltInCategory> CheckedCategories()
        {
            return _categories.CheckedItems.Cast<CategoryItem>().Select(i => i.Category).ToList();
        }

        private void Rescan()
        {
            _rows = DeviceScanner.Scan(_document, CheckedCategories(), _settings);

            int withCode = _rows.Count(r => !string.IsNullOrWhiteSpace(r.Code));
            _foundSummary.Text =
                "Найдено типов: " + _rows.Count.ToString(CultureInfo.CurrentCulture) +
                ", экземпляров: " + _rows.Sum(r => r.Count).ToString(CultureInfo.CurrentCulture) + ".\n" +
                "С буквенным кодом в марке: " + withCode.ToString(CultureInfo.CurrentCulture) +
                " — только они по умолчанию попадут в легенду.";

            FillGrid();
        }

        // ===== Шаг 2: коды и наименования =====

        private void BuildStepCodes(Control page)
        {
            page.Controls.Add(Caption("Шаг 2. Как оборудование называется в таблице", 0));

            var hint = new Label
            {
                Text = "Код — префикс марки, наименование — ADSK_Наименование. И то, и другое правится прямо здесь: " +
                       "в марках попадаются опечатки, а паспортный текст в колонку не помещается. Правки хранятся в модели.",
                Dock = DockStyle.Top,
                Height = 36,
                Padding = new Padding(0, 26, 0, 0),
                ForeColor = SystemColors.GrayText
            };

            _onlyWithCode.Text = "Показывать только строки с кодом";
            _onlyWithCode.Dock = DockStyle.Top;
            _onlyWithCode.Height = 24;
            _onlyWithCode.Checked = true;
            _onlyWithCode.CheckedChanged += (s, e) => FillGrid();

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = string.Empty, Width = 30, Name = "include" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Код", Width = 70, Name = "code" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Наименование", Width = 420, Name = "description" });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Тип", Width = 250, Name = "type", ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шт.", Width = 50, Name = "count", ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Замечание", Width = 240, Name = "problem", ReadOnly = true });

            _grid.CellValueChanged += (s, e) => { if (e.RowIndex >= 0) RefreshRowsSummary(); };
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Галка в таблице доходит до обработчика только после ухода из ячейки — счётчик
                // отставал бы на один клик.
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _rowsSummary.Dock = DockStyle.Bottom;
            _rowsSummary.Height = 22;

            page.Controls.Add(_grid);
            page.Controls.Add(_rowsSummary);
            page.Controls.Add(_onlyWithCode);
            page.Controls.Add(hint);
        }

        private void FillGrid()
        {
            if (_grid.Columns.Count == 0) return;

            _grid.Rows.Clear();

            foreach (DeviceRow row in _rows)
            {
                if (_onlyWithCode.Checked && string.IsNullOrWhiteSpace(row.Code)) continue;

                int index = _grid.Rows.Add(
                    row.Include,
                    row.Code,
                    row.Description,
                    row.ToString(),
                    row.Count.ToString(CultureInfo.CurrentCulture),
                    row.Problem);

                DataGridViewRow gridRow = _grid.Rows[index];
                gridRow.Tag = row;

                if (!string.IsNullOrEmpty(row.Problem)) gridRow.DefaultCellStyle.ForeColor = Color.Firebrick;
            }

            RefreshRowsSummary();
        }

        /// <summary>
        /// Считает отмеченное по самой таблице, а не по строкам модели: галку сняли только что,
        /// в строки она попадёт при переходе на следующий шаг, а счётчик обязан меняться сразу —
        /// иначе непонятно, приняли снятие галки или нет.
        /// </summary>
        private void RefreshRowsSummary()
        {
            int checkedRows = 0;
            int devices = 0;

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!(gridRow.Cells["include"].Value is bool included) || !included) continue;

                checkedRows++;

                var row = gridRow.Tag as DeviceRow;
                if (row != null) devices += row.Count;
            }

            _rowsSummary.Text = "Строк в таблице: " + _grid.Rows.Count.ToString(CultureInfo.CurrentCulture) +
                                ", отмечено: " + checkedRows.ToString(CultureInfo.CurrentCulture) +
                                ", устройств в них: " + devices.ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>Переносит правки из таблицы в строки. Скрытые фильтром строки не трогаются.</summary>
        private void ReadGrid()
        {
            _grid.EndEdit();

            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (!(gridRow.Tag is DeviceRow row)) continue;

                row.Include = Convert.ToBoolean(gridRow.Cells["include"].Value, CultureInfo.InvariantCulture);
                row.Code = Convert.ToString(gridRow.Cells["code"].Value, CultureInfo.CurrentCulture) ?? string.Empty;
                row.Description = Convert.ToString(gridRow.Cells["description"].Value, CultureInfo.CurrentCulture) ?? string.Empty;
            }
        }

        // ===== Шаг 3: зоны =====

        private void BuildStepZones(Control page)
        {
            page.Controls.Add(Caption("Шаг 3. Чем считать зону", 0));

            var hint = new Label
            {
                Text = "Зона — колонка будущей матрицы: квартира, коридор, лифтовый холл, ЩТС. Помещения ищутся и в " +
                       "этой модели, и в связях. Для легенды зоны не нужны — этот шаг готовит данные для схемы.",
                Location = new Point(0, 26),
                Size = new Size(860, 34),
                ForeColor = SystemColors.GrayText
            };

            var zoningLabel = new Label { Text = "Зона по:", Location = new Point(0, 66), AutoSize = true };
            _zoning.DropDownStyle = ComboBoxStyle.DropDownList;
            _zoning.Location = new Point(0, 86);
            _zoning.Width = 240;
            _zoning.SelectedIndexChanged += (s, e) => OnZoningChanged();

            _zoneParameterLabel.Text = "Имя параметра:";
            _zoneParameterLabel.Location = new Point(256, 66);
            _zoneParameterLabel.Size = new Size(240, 16);

            _zoneParameter.Location = new Point(256, 86);
            _zoneParameter.Width = 240;

            var check = new Button { Text = "Проверить", Location = new Point(512, 84), Size = new Size(120, 26) };
            check.Click += (s, e) => RecalculateZones();

            _zonesGrid.Location = new Point(0, 126);
            _zonesGrid.Size = new Size(820, 250);
            _zonesGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _zonesGrid.AllowUserToAddRows = false;
            _zonesGrid.AllowUserToDeleteRows = false;
            _zonesGrid.ReadOnly = true;
            _zonesGrid.RowHeadersVisible = false;
            _zonesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _zonesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Этаж", Width = 200 });
            _zonesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Отметка", Width = 80 });
            _zonesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Зона", Width = 240 });
            _zonesGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Устройств", Width = 90 });

            _zonesSummary.Dock = DockStyle.Bottom;
            _zonesSummary.Height = 22;

            _zonesNotes.Dock = DockStyle.Bottom;
            _zonesNotes.Height = 36;
            _zonesNotes.ForeColor = SystemColors.GrayText;

            page.Controls.Add(_zonesGrid);
            page.Controls.Add(hint);
            page.Controls.Add(zoningLabel);
            page.Controls.Add(_zoning);
            page.Controls.Add(_zoneParameterLabel);
            page.Controls.Add(_zoneParameter);
            page.Controls.Add(check);
            page.Controls.Add(_zonesSummary);
            page.Controls.Add(_zonesNotes);
        }

        private void FillZoning()
        {
            _zoning.Items.Add(new ZoningItem(ZoneSource.ApartmentOrRoom, "Квартире, иначе помещению"));
            _zoning.Items.Add(new ZoningItem(ZoneSource.RoomName, "Имени помещения"));
            _zoning.Items.Add(new ZoningItem(ZoneSource.RoomNumber, "Номеру помещения"));
            _zoning.Items.Add(new ZoningItem(ZoneSource.RoomParameter, "Параметру помещения"));
            _zoning.Items.Add(new ZoningItem(ZoneSource.DeviceParameter, "Параметру устройства"));

            _zoning.SelectedIndex = _zoning.Items.Cast<ZoningItem>().ToList()
                .FindIndex(i => i.Source == _settings.Zoning);

            if (_zoning.SelectedIndex < 0) _zoning.SelectedIndex = 0;

            OnZoningChanged();
        }

        private ZoneSource SelectedZoning => ((ZoningItem)_zoning.SelectedItem).Source;

        /// <summary>
        /// Поле параметра одно, но смысл у него разный: для квартир это параметр с её номером,
        /// для остальных режимов — параметр зоны. Подпись меняется вместе со смыслом.
        /// </summary>
        private void OnZoningChanged()
        {
            ZoneSource zoning = SelectedZoning;

            switch (zoning)
            {
                case ZoneSource.ApartmentOrRoom:
                    _zoneParameterLabel.Text = "Параметр помещения с номером квартиры:";
                    _zoneParameter.Enabled = true;
                    _zoneParameter.Text = _settings.ApartmentParameterName;
                    break;

                case ZoneSource.RoomParameter:
                case ZoneSource.DeviceParameter:
                    _zoneParameterLabel.Text = "Имя параметра:";
                    _zoneParameter.Enabled = true;
                    _zoneParameter.Text = _settings.ZoneParameterName;
                    break;

                default:
                    _zoneParameterLabel.Text = "Имя параметра:";
                    _zoneParameter.Enabled = false;
                    break;
            }
        }

        /// <summary>Складывает содержимое поля параметра в то свойство, которому оно сейчас отвечает.</summary>
        private void ReadZoneParameter()
        {
            string value = _zoneParameter.Text.Trim();

            if (SelectedZoning == ZoneSource.ApartmentOrRoom)
            {
                _settings.ApartmentParameterName = value;
                return;
            }

            if (_zoneParameter.Enabled) _settings.ZoneParameterName = value;
        }

        private void RecalculateZones()
        {
            _settings.Zoning = SelectedZoning;
            ReadZoneParameter();

            _zones = SchemeScanner.Scan(_document, _settings, _rows);

            _zonesGrid.Rows.Clear();

            foreach (FloorGroup floor in _zones.Floors)
            {
                string elevation = Millimeters(floor.Elevation);

                foreach (ZoneGroup zone in floor.Zones)
                {
                    int index = _zonesGrid.Rows.Add(
                        floor.Name,
                        elevation,
                        zone.Name,
                        zone.Total.ToString(CultureInfo.CurrentCulture));

                    if (zone.Name == SchemeScanner.NoZone)
                    {
                        _zonesGrid.Rows[index].DefaultCellStyle.ForeColor = Color.Firebrick;
                    }
                }
            }

            _zonesSummary.Text = "Этажей: " + _zones.Floors.Count.ToString(CultureInfo.CurrentCulture) +
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

            _zonesNotes.Text = notes.ToString();
        }

        // ===== Шаг 4: оформление =====

        private void BuildStepLayout(Control page)
        {
            page.Controls.Add(Caption("Шаг 4. Легенда УГО: где и как строить таблицу", 0));

            _buildLegend.Text = "Строить легенду УГО";
            _buildLegend.Location = new Point(0, 28);
            _buildLegend.Size = new Size(300, 20);
            _buildLegend.Checked = _settings.BuildLegend;
            _buildLegend.CheckedChanged += (s, e) => EnableLegendControls();

            _lineLegend.Text = "Добавить блок типов линий (АЛС, ЛСО, ЛРО, питание)";
            _lineLegend.Location = new Point(320, 28);
            _lineLegend.Size = new Size(400, 20);
            _lineLegend.Checked = _settings.BuildLineLegend;

            var legendLabel = new Label { Text = "Легенда:", Location = new Point(0, 60), AutoSize = true };
            _legendPicker.DropDownStyle = ComboBoxStyle.DropDownList;
            _legendPicker.Location = new Point(0, 80);
            _legendPicker.Width = 320;
            _legendPicker.SelectedIndexChanged += (s, e) => RefreshTemplateStatus();

            var textLabel = new Label { Text = "Тип текста (им набираются и легенда, и схема):", Location = new Point(360, 60), AutoSize = true };
            _textType.DropDownStyle = ComboBoxStyle.DropDownList;
            _textType.Location = new Point(360, 80);
            _textType.Width = 320;

            _templateStatus.Location = new Point(0, 114);
            _templateStatus.Size = new Size(860, 54);

            page.Controls.Add(textLabel);
            page.Controls.Add(_textType);

            var sizes = new Label
            {
                Text = "Размеры на листе, мм. Высота строки — минимальная: под длинное наименование строка вырастет сама.",
                Location = new Point(0, 178),
                Size = new Size(860, 20),
                ForeColor = SystemColors.GrayText
            };

            AddSpin(page, "Высота строки", _rowHeight, 0, 206);
            AddSpin(page, "Колонка УГО", _symbolWidth, 150, 206);
            AddSpin(page, "Колонка кода", _codeWidth, 300, 206);
            AddSpin(page, "Колонка наименования", _descriptionWidth, 450, 206);
            AddSpin(page, "Строк в колонке", _rowsPerColumn, 620, 206);

            page.Controls.Add(_buildLegend);
            page.Controls.Add(_lineLegend);
            page.Controls.Add(legendLabel);
            page.Controls.Add(_legendPicker);
            page.Controls.Add(_templateStatus);
            page.Controls.Add(sizes);

            _legendControls.AddRange(new Control[]
            {
                legendLabel, _legendPicker, _templateStatus, sizes, _lineLegend,
                _rowHeight, _symbolWidth, _codeWidth, _descriptionWidth, _rowsPerColumn
            });
        }

        /// <summary>
        /// Выключенная легенда гасит свои настройки: иначе непонятно, почему заданные размеры
        /// ни на что не влияют.
        /// </summary>
        private void EnableLegendControls()
        {
            foreach (Control control in _legendControls) control.Enabled = _buildLegend.Checked;
        }

        private void FillLegends()
        {
            foreach (View legend in _legends) _legendPicker.Items.Add(new LegendItem(legend));

            if (_legends.Count == 0)
            {
                // Легенду создать из кода нельзя — без неё таблицу строить нечем. Схеме это не
                // мешает, поэтому мастер не закрывается, а просто выключает легенду.
                _buildLegend.Checked = false;
                _buildLegend.Enabled = false;
                _buildLegend.Text = "Строить легенду УГО — в проекте нет ни одной легенды";
                return;
            }

            int saved = _legends.FindIndex(v => v.Id == _settings.LegendViewId);
            _legendPicker.SelectedIndex = saved >= 0 ? saved : 0;
        }

        private void RefreshTemplateStatus()
        {
            View legend = SelectedLegend;
            if (legend == null)
            {
                _templateStatus.Text = string.Empty;
                return;
            }

            LegendBuilder.Template template = LegendBuilder.FindTemplate(_document, legend);
            string scale = " Масштаб вида 1:" + legend.Scale.ToString(CultureInfo.CurrentCulture) + ".";

            if (template == null)
            {
                _templateStatus.ForeColor = Color.Firebrick;
                _templateStatus.Text =
                    "В проекте нет ни одного компонента легенды — колонка УГО останется пустой.\n" +
                    "Символ рисует штатный «Компонент легенды», а создать его из кода Revit не даёт. Вставьте " +
                    "в любую легенду один компонент (Аннотации → Компонент легенды) и вернитесь сюда." + scale;
                return;
            }

            _templateStatus.ForeColor = Color.DarkGreen;
            _templateStatus.Text = template.Source.Id == legend.Id
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
                _textType.Items.Add(new TextTypeItem(type));
            }

            List<TextTypeItem> items = _textType.Items.Cast<TextTypeItem>().ToList();

            int saved = items.FindIndex(i => i.Id == _settings.TextTypeId);
            if (saved < 0) saved = items.FindIndex(i => i.Id == fallback);
            if (saved < 0 && items.Count > 0) saved = 0;

            if (saved >= 0) _textType.SelectedIndex = saved;
        }

        private void LoadSizes()
        {
            _rowHeight.Value = Clamp(_rowHeight, (decimal)_settings.RowHeightMm);
            _symbolWidth.Value = Clamp(_symbolWidth, (decimal)_settings.SymbolWidthMm);
            _codeWidth.Value = Clamp(_codeWidth, (decimal)_settings.CodeWidthMm);
            _descriptionWidth.Value = Clamp(_descriptionWidth, (decimal)_settings.DescriptionWidthMm);
            _rowsPerColumn.Value = Clamp(_rowsPerColumn, _settings.RowsPerColumn);

            _matrixScale.Value = Clamp(_matrixScale, _settings.MatrixScale);
            _matrixBlock.Value = Clamp(_matrixBlock, (decimal)_settings.MatrixBlockMm);
            _matrixBlockHeight.Value = Clamp(_matrixBlockHeight, (decimal)_settings.MatrixBlockHeightMm);
            _matrixPerLine.Value = Clamp(_matrixPerLine, _settings.MatrixBlocksPerLine);
            _matrixFloorColumn.Value = Clamp(_matrixFloorColumn, (decimal)_settings.MatrixFloorColumnMm);

            EnableLegendControls();
            EnableMatrixControls();
        }

        // ===== Шаг 5: схема =====

        private void BuildStepMatrix(Control page)
        {
            page.Controls.Add(Caption("Шаг 5. Матрица схемы", 0));

            var hint = new Label
            {
                Text = "Строки — этажи сверху вниз, в строке ячейки помещений: имя сверху, под ним блоки «код и " +
                       "количество». У подвала и первого этажа помещения свои, поэтому сетка не сквозная — " +
                       "строки просто растянуты до общей ширины.",
                Location = new Point(0, 26),
                Size = new Size(880, 34),
                ForeColor = SystemColors.GrayText
            };

            _buildMatrix.Text = "Чертить матрицу схемы";
            _buildMatrix.Location = new Point(0, 62);
            _buildMatrix.Size = new Size(220, 20);
            _buildMatrix.Checked = _settings.BuildMatrix;
            _buildMatrix.CheckedChanged += (s, e) => EnableMatrixControls();

            _collapseTypical.Text = "Сводить одинаковые этажи в строку «2–16 этаж (тип.)»";
            _collapseTypical.Location = new Point(240, 62);
            _collapseTypical.Size = new Size(360, 20);
            _collapseTypical.Checked = _settings.CollapseTypicalFloors;
            _collapseTypical.CheckedChanged += (s, e) => RefreshMatrix();

            var orderLabel = new Label { Text = "Порядок ячеек:", Location = new Point(610, 62), AutoSize = true };
            _cellOrder.DropDownStyle = ComboBoxStyle.DropDownList;
            _cellOrder.Location = new Point(700, 59);
            _cellOrder.Width = 190;
            _cellOrder.Items.Add(new OrderItem(ZoneOrder.AlongX, "по X — дом вдоль X"));
            _cellOrder.Items.Add(new OrderItem(ZoneOrder.AlongY, "по Y — дом поперёк"));
            _cellOrder.Items.Add(new OrderItem(ZoneOrder.SideThenX, "стороны, затем X"));
            _cellOrder.Items.Add(new OrderItem(ZoneOrder.SideThenY, "стороны, затем Y"));
            _cellOrder.Items.Add(new OrderItem(ZoneOrder.ByName, "по имени"));
            _cellOrder.SelectedIndex = Math.Max(0, _cellOrder.Items.Cast<OrderItem>().ToList()
                .FindIndex(i => i.Order == _settings.MatrixOrder));
            _cellOrder.SelectedIndexChanged += (s, e) => RefreshMatrix();

            _orderReversed.Text = "в обратную сторону";
            _orderReversed.Location = new Point(700, 86);
            _orderReversed.Size = new Size(190, 20);
            _orderReversed.Checked = _settings.MatrixOrderReversed;
            _orderReversed.CheckedChanged += (s, e) => RefreshMatrix();

            var nameLabel = new Label { Text = "Черновой вид:", Location = new Point(0, 92), AutoSize = true };
            _matrixViewName.Location = new Point(0, 112);
            _matrixViewName.Width = 300;
            _matrixViewName.Text = _settings.MatrixViewName;

            _drawUgo.Text = UgoCaption();
            _drawUgo.Location = new Point(0, 138);
            _drawUgo.Size = new Size(430, 20);
            _drawUgo.Checked = _settings.MatrixDrawUgo;

            _drawTrunks.Text = "Чертить магистрали (АЛС, ЛСО, ЛРО, питание)";
            _drawTrunks.Location = new Point(440, 138);
            _drawTrunks.Size = new Size(320, 20);
            _drawTrunks.Checked = _settings.MatrixDrawTrunks;

            _showUnzoned.Text = "Показывать «зона не определена»";
            _showUnzoned.Location = new Point(440, 160);
            _showUnzoned.Size = new Size(320, 20);
            _showUnzoned.Checked = _settings.MatrixShowUnzoned;
            _showUnzoned.CheckedChanged += (s, e) => RefreshMatrix();

            var trunkLabel = new Label { Text = "Стояки:", Location = new Point(0, 160), AutoSize = true };
            _trunkPosition.DropDownStyle = ComboBoxStyle.DropDownList;
            _trunkPosition.Location = new Point(60, 157);
            _trunkPosition.Width = 150;
            _trunkPosition.Items.Add(new TrunkItem(TrunkPosition.Middle, "посередине"));
            _trunkPosition.Items.Add(new TrunkItem(TrunkPosition.Left, "слева"));
            _trunkPosition.Items.Add(new TrunkItem(TrunkPosition.Right, "справа"));
            _trunkPosition.SelectedIndex = Math.Max(0, _trunkPosition.Items.Cast<TrunkItem>().ToList()
                .FindIndex(i => i.Position == _settings.MatrixTrunkPosition));
            _trunkPosition.SelectedIndexChanged += (s, e) => RefreshMatrix();

            var panelLabel = new Label { Text = "Щит — своя ячейка, если в имени типа есть:", Location = new Point(230, 160), AutoSize = true };
            _panelPattern.Location = new Point(230, 180);
            _panelPattern.Width = 190;
            _panelPattern.Text = _settings.MatrixPanelPattern;

            var nameHint = new Label
            {
                Text = "Вид создаётся плагином. Если вид с таким именем уже есть — схема строится в нём.",
                Location = new Point(0, 160),
                Size = new Size(620, 18),
                ForeColor = SystemColors.GrayText
            };

            AddSpin(page, "Масштаб 1:", _matrixScale, 320, 92);
            AddSpin(page, "Блок, мм", _matrixBlock, 430, 92);
            AddSpin(page, "Высота блока", _matrixBlockHeight, 540, 92);
            AddSpin(page, "Блоков в строке", _matrixPerLine, 660, 92);
            AddSpin(page, "Колонка этажа", _matrixFloorColumn, 780, 92);

            _matrixScale.ValueChanged += (s, e) => RefreshMatrixSummary();
            _matrixBlock.ValueChanged += (s, e) => RefreshMatrix();
            _matrixBlockHeight.ValueChanged += (s, e) => RefreshMatrixSummary();
            _matrixPerLine.ValueChanged += (s, e) => RefreshMatrix();
            _matrixFloorColumn.ValueChanged += (s, e) => RefreshMatrixSummary();

            _matrixGrid.Location = new Point(0, 186);
            _matrixGrid.Size = new Size(880, 230);
            _matrixGrid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            _matrixGrid.AllowUserToAddRows = false;
            _matrixGrid.AllowUserToDeleteRows = false;
            _matrixGrid.AllowUserToResizeRows = false;
            _matrixGrid.ReadOnly = true;
            _matrixGrid.RowHeadersVisible = false;
            _matrixGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;

            _matrixSummary.Dock = DockStyle.Bottom;
            _matrixSummary.Height = 40;

            page.Controls.Add(_matrixGrid);
            page.Controls.Add(hint);
            page.Controls.Add(_buildMatrix);
            page.Controls.Add(_collapseTypical);
            page.Controls.Add(orderLabel);
            page.Controls.Add(_cellOrder);
            page.Controls.Add(_orderReversed);
            page.Controls.Add(nameLabel);
            page.Controls.Add(_matrixViewName);
            page.Controls.Add(_drawUgo);
            page.Controls.Add(_drawTrunks);
            page.Controls.Add(_showUnzoned);
            page.Controls.Add(trunkLabel);
            page.Controls.Add(_trunkPosition);
            page.Controls.Add(panelLabel);
            page.Controls.Add(_panelPattern);
            page.Controls.Add(nameHint);
            page.Controls.Add(_matrixSummary);

            _matrixControls.AddRange(new Control[]
            {
                _collapseTypical, orderLabel, _cellOrder, _orderReversed, nameLabel, _matrixViewName,
                nameHint, _drawUgo, _drawTrunks, _showUnzoned, trunkLabel, _trunkPosition, panelLabel, _panelPattern, _matrixScale, _matrixBlock, _matrixBlockHeight, _matrixPerLine,
                _matrixFloorColumn, _matrixGrid
            });
        }

        private void EnableMatrixControls()
        {
            foreach (Control control in _matrixControls) control.Enabled = _buildMatrix.Checked;
        }

        /// <summary>
        /// Список штампов проекта. Выбирать за пользователя нельзя: ширина листа у этих
        /// семейств не заполнена, и любое автоматическое правило промахивается — в первый раз
        /// в рамку попал служебный «Начальный вид».
        /// </summary>
        private void FillTitleBlocks()
        {
            foreach (Autodesk.Revit.DB.FamilySymbol block in SheetBuilder.TitleBlocks(_document))
            {
                _titleBlock.Items.Add(SheetBuilder.Caption(block));
            }

            int saved = string.IsNullOrWhiteSpace(_settings.SheetTitleBlock)
                ? -1
                : _titleBlock.Items.Cast<string>().ToList()
                    .FindIndex(i => i.IndexOf(_settings.SheetTitleBlock, StringComparison.CurrentCultureIgnoreCase) >= 0);

            if (saved < 0 && _titleBlock.Items.Count > 0) saved = 0;
            if (saved >= 0) _titleBlock.SelectedIndex = saved;
        }

        private TrunkPosition SelectedTrunkPosition =>
            (_trunkPosition.SelectedItem as TrunkItem)?.Position ?? TrunkPosition.Middle;

        private ZoneOrder SelectedOrder => (_cellOrder.SelectedItem as OrderItem)?.Order ?? ZoneOrder.AlongX;

        /// <summary>
        /// Подпись галки УГО прямо говорит, сопоставлено ли хоть что-то: сопоставление делается
        /// не здесь, а кнопкой на ленте, и без подсказки непонятно, почему блоки текстовые.
        /// </summary>
        private string UgoCaption()
        {
            return _settings.UgoCount > 0
                ? "Ставить УГО в блоки (сопоставлено семейств: " +
                  _settings.UgoCount.ToString(CultureInfo.CurrentCulture) + ")"
                : "Ставить УГО в блоки — сопоставления нет, сделайте его кнопкой «УГО» на ленте";
        }

        /// <summary>
        /// Пересобирает раскладку и показывает её как есть: колонки грида — колонки схемы,
        /// строки — строки схемы. Смотреть на цифры сводки и на будущий лист приходится вместе,
        /// поэтому это одна и та же таблица, а не две разные.
        /// </summary>
        private void RefreshMatrix()
        {
            _matrix = MatrixLayout.Build(_zones, MatrixSizes(), _collapseTypical.Checked);

            _matrixGrid.Rows.Clear();
            _matrixGrid.Columns.Clear();

            _matrixGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Этаж", Width = 170, Frozen = true });

            // Колонки грида — места в строке, а не имена: у подвала в третьей ячейке своё
            // помещение, у типового этажа своё, и общей шапки у них нет.
            int widest = _matrix.Bands.Count == 0 ? 0 : _matrix.Bands.Max(b => b.Cells.Count);

            for (int index = 0; index < widest; index++)
            {
                _matrixGrid.Columns.Add(new DataGridViewTextBoxColumn
                {
                    HeaderText = (index + 1).ToString(CultureInfo.CurrentCulture),
                    Width = 170
                });
            }

            foreach (MatrixBand band in _matrix.Bands)
            {
                var values = new List<object> { band.Caption };

                // В ячейке грида перевод строки не показать — блоки идут через точку с запятой.
                foreach (MatrixCell cell in band.Cells)
                {
                    values.Add(cell.Title + " — " +
                               string.Join("; ", cell.Blocks.Select(b => b.Caption + " " + b.Amount)));
                }

                int row = _matrixGrid.Rows.Add(values.ToArray());
                if (band.IsTypical) _matrixGrid.Rows[row].DefaultCellStyle.BackColor = Color.FromArgb(240, 245, 235);

                for (int index = 0; index < band.Cells.Count; index++)
                {
                    if (band.Cells[index].Key != SchemeScanner.NoZone) continue;

                    _matrixGrid.Rows[row].Cells[index + 1].Style.ForeColor = Color.Firebrick;
                }
            }

            RefreshMatrixSummary();
            EnableMatrixControls();
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

            _matrixSummary.Text = text.ToString();
            _matrixSummary.ForeColor = width > 1189 ? Color.Firebrick : SystemColors.ControlText;
        }

        /// <summary>Размеры схемы, как они заданы прямо сейчас, — нужны и превью, и построению.</summary>
        private SchemeSettings MatrixSizes()
        {
            return new SchemeSettings
            {
                MatrixBlockMm = (double)_matrixBlock.Value,
                MatrixBlockHeightMm = (double)_matrixBlockHeight.Value,
                MatrixBlocksPerLine = (int)_matrixPerLine.Value,
                MatrixFloorColumnMm = (double)_matrixFloorColumn.Value,
                MatrixShowUnzoned = _showUnzoned.Checked,
                MatrixTrunkPosition = SelectedTrunkPosition,
                MatrixDrawTrunks = _drawTrunks.Checked,
                MatrixOrder = SelectedOrder,
                MatrixOrderReversed = _orderReversed.Checked,
                MatrixScale = (int)_matrixScale.Value
            };
        }

        // ===== Шаг 6: построение =====

        private void BuildStepFinish(Control page)
        {
            page.Controls.Add(Caption("Шаг 6. Что будет построено", 0));

            _placeOnSheet.Text = "Положить схему на лист с основной надписью";
            _placeOnSheet.Location = new Point(0, 28);
            _placeOnSheet.Size = new Size(360, 20);
            _placeOnSheet.Checked = _settings.PlaceOnSheet;
            _placeOnSheet.CheckedChanged += (s, e) => RefreshFinalSummary();

            var numberLabel = new Label { Text = "Номер листа:", Location = new Point(0, 54), AutoSize = true };
            _sheetNumber.Location = new Point(0, 74);
            _sheetNumber.Width = 110;
            _sheetNumber.Text = _settings.SheetNumber;

            var nameLabel = new Label { Text = "Имя листа:", Location = new Point(130, 54), AutoSize = true };
            _sheetName.Location = new Point(130, 74);
            _sheetName.Width = 300;
            _sheetName.Text = _settings.SheetName;

            var blockLabel = new Label { Text = "Основная надпись:", Location = new Point(450, 54), AutoSize = true };
            _titleBlock.DropDownStyle = ComboBoxStyle.DropDownList;
            _titleBlock.Location = new Point(450, 74);
            _titleBlock.Width = 380;
            FillTitleBlocks();

            var sheetHint = new Label
            {
                Text = "Лист заводится один раз и дальше переиспользуется: вид можно положить только на один лист. " +
                       "Штамп на готовом листе меняется, если выбрать здесь другой.",
                Location = new Point(0, 100),
                Size = new Size(880, 34),
                ForeColor = SystemColors.GrayText
            };

            page.Controls.Add(blockLabel);
            page.Controls.Add(_titleBlock);

            _finalSummary.Location = new Point(0, 142);
            _finalSummary.Size = new Size(880, 260);

            page.Controls.Add(_placeOnSheet);
            page.Controls.Add(numberLabel);
            page.Controls.Add(_sheetNumber);
            page.Controls.Add(nameLabel);
            page.Controls.Add(_sheetName);
            page.Controls.Add(sheetHint);
            page.Controls.Add(_finalSummary);
        }

        private void RefreshFinalSummary()
        {
            View legend = SelectedLegend;
            bool hasTemplate = legend != null && LegendBuilder.FindTemplateComponent(_document, legend) != null;
            int included = _rows.Count(r => r.Include);

            var text = new StringBuilder();

            if (_buildLegend.Checked)
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

            if (_buildMatrix.Checked)
            {
                text.AppendLine("Матрица схемы");
                text.AppendLine("    строк: " + _matrix.Bands.Count.ToString(CultureInfo.CurrentCulture) +
                                " (типовых: " + _matrix.Bands.Count(b => b.IsTypical).ToString(CultureInfo.CurrentCulture) + ")");
                text.AppendLine("    ячеек: " + _matrix.Cells.ToString(CultureInfo.CurrentCulture) +
                                ", устройств: " + _matrix.Devices.ToString(CultureInfo.CurrentCulture));
                text.AppendLine("    вид: " + _matrixViewName.Text.Trim() + ", 1:" +
                                ((int)_matrixScale.Value).ToString(CultureInfo.CurrentCulture));
                text.AppendLine("    на листе: " + MatrixBuilder.SizeCaption(_matrix, MatrixSizes()));

                if (_drawUgo.Checked && _settings.UgoCount == 0)
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
            if (_buildMatrix.Checked && _placeOnSheet.Checked)
            {
                text.AppendLine("Лист " + _sheetNumber.Text.Trim() + " «" + _sheetName.Text.Trim() + "» — схема ляжет на него.");
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

            _finalSummary.Text = text.ToString();
        }

        /// <summary>Складывает выбор пользователя в настройки — их сохранит команда в транзакции.</summary>
        private void Collect()
        {
            ReadGrid();

            foreach (DeviceRow row in _rows) _settings.Remember(row, row.ModelCode, row.ModelDescription);

            _settings.Categories.Clear();
            foreach (BuiltInCategory category in CheckedCategories()) _settings.Categories.Add(category);

            _settings.LegendViewId = SelectedLegend?.Id ?? ElementId.InvalidElementId;
            _settings.Zoning = SelectedZoning;
            ReadZoneParameter();
            _settings.TextTypeId = (_textType.SelectedItem as TextTypeItem)?.Id ?? ElementId.InvalidElementId;
            _settings.RowHeightMm = (double)_rowHeight.Value;
            _settings.SymbolWidthMm = (double)_symbolWidth.Value;
            _settings.CodeWidthMm = (double)_codeWidth.Value;
            _settings.DescriptionWidthMm = (double)_descriptionWidth.Value;
            _settings.RowsPerColumn = (int)_rowsPerColumn.Value;

            _settings.BuildLegend = _buildLegend.Checked;
            _settings.BuildLineLegend = _lineLegend.Checked;
            _settings.BuildMatrix = _buildMatrix.Checked;
            _settings.CollapseTypicalFloors = _collapseTypical.Checked;
            _settings.MatrixViewName = _matrixViewName.Text.Trim();
            _settings.MatrixScale = (int)_matrixScale.Value;
            _settings.MatrixBlockMm = (double)_matrixBlock.Value;
            _settings.MatrixBlockHeightMm = (double)_matrixBlockHeight.Value;
            _settings.MatrixBlocksPerLine = (int)_matrixPerLine.Value;
            _settings.MatrixFloorColumnMm = (double)_matrixFloorColumn.Value;
            _settings.MatrixOrder = SelectedOrder;
            _settings.MatrixOrderReversed = _orderReversed.Checked;
            _settings.MatrixDrawUgo = _drawUgo.Checked;
            _settings.MatrixDrawTrunks = _drawTrunks.Checked;
            _settings.MatrixShowUnzoned = _showUnzoned.Checked;
            _settings.MatrixTrunkPosition = SelectedTrunkPosition;
            _settings.MatrixPanelPattern = _panelPattern.Text.Trim();
            _settings.PlaceOnSheet = _placeOnSheet.Checked;
            _settings.SheetNumber = _sheetNumber.Text.Trim();
            _settings.SheetName = _sheetName.Text.Trim();
            _settings.SheetTitleBlock = _titleBlock.SelectedItem as string ?? string.Empty;
        }

        // ===== Мелочи =====

        private static Label Caption(string text, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(0, y),
                Size = new Size(700, 22),
                Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
            };
        }

        private static void AddSpin(Control parent, string caption, NumericUpDown spin, int x, int y)
        {
            parent.Controls.Add(new Label { Text = caption, Location = new Point(x, y), Size = new Size(150, 16) });
            spin.Location = new Point(x, y + 18);
            spin.Width = 80;
            parent.Controls.Add(spin);
        }

        private static NumericUpDown Spin(int min, int max)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = 0 };
        }

        private static decimal Clamp(NumericUpDown spin, decimal value)
        {
            return Math.Min(spin.Maximum, Math.Max(spin.Minimum, value));
        }

        private static string Millimeters(double feet)
        {
            double millimeters = UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
            return millimeters.ToString("+#,##0;-#,##0;0", CultureInfo.CurrentCulture);
        }

        private class CategoryItem
        {
            public CategoryItem(BuiltInCategory category, int count)
            {
                Category = category;
                Count = count;
            }

            public BuiltInCategory Category { get; }

            private int Count { get; }

            public override string ToString()
            {
                string name;
                try
                {
                    name = LabelUtils.GetLabelFor(Category);
                }
                catch (Exception)
                {
                    name = Category.ToString();
                }

                return name + "  —  " + Count.ToString(CultureInfo.CurrentCulture);
            }
        }

        private class LegendItem
        {
            public LegendItem(View view)
            {
                View = view;
            }

            public View View { get; }

            public override string ToString() => View.Name + "   (1:" + View.Scale.ToString(CultureInfo.CurrentCulture) + ")";
        }

        /// <summary>Высота в имени: от неё зависит, как ляжет строка таблицы.</summary>
        private class TextTypeItem
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

        /// <summary>Строка списка «стояки».</summary>
        private class TrunkItem
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

        /// <summary>Строка списка «порядок ячеек».</summary>
        private class OrderItem
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

        private class ZoningItem
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
}
