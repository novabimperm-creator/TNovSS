using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using SchemeBuilder.Core;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;
using Panel = System.Windows.Forms.Panel;

namespace SchemeBuilder.UI
{
    /// <summary>
    /// Сопоставление «прибор → его УГО на схеме».
    ///
    /// Отдельное окно, а не шаг конструктора: делается один раз на модель, а проверять его надо
    /// глазами и целиком — сорок строк подряд, а не по одной между другими решениями.
    /// </summary>
    internal class UgoForm : Form
    {
        private const int ColumnCode = 0;
        private const int ColumnType = 1;
        private const int ColumnCount = 2;
        private const int ColumnDeclared = 3;
        private const int ColumnUgo = 4;
        private const int ColumnState = 5;

        private const string NoUgo = "— без УГО —";

        private readonly Document _document;
        private readonly SchemeSettings _settings;
        private readonly List<DeviceRow> _rows;

        private readonly DataGridView _grid = new DataGridView();
        private readonly Label _summary = new Label();

        private List<UgoCandidate> _candidates = new List<UgoCandidate>();
        private List<UgoMatch> _matches = new List<UgoMatch>();

        public UgoForm(Document document, SchemeSettings settings, List<DeviceRow> rows)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));

            Text = "Структурная схема — УГО оборудования";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1000, 560);
            Size = new Size(1120, 660);
            Font = SystemFonts.MessageBoxFont;

            BuildLayout();
            Fill();
        }

        /// <summary>Что выбрано. Сохраняет выбор команда — форме транзакции не положены.</summary>
        public List<UgoMatch> Matches => _matches;

        private void BuildLayout()
        {
            var hint = new Label
            {
                Text = "УГО схемы — это аннотации проекта (RBZ_УГО-2D_Узел_*): ими начерчены выпущенные листы, " +
                       "и подписи «Марка» и «N шт.» у них уже внутри. Плагин подставляет УГО по параметру типа " +
                       "«Типоразмер УГО»; подставленное помечено серым — проверьте и поправьте, где не угадал.",
                Dock = DockStyle.Top,
                Height = 52,
                Padding = new Padding(14, 10, 14, 0),
                ForeColor = SystemColors.GrayText
            };

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Код", Width = 70, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Типоразмер", Width = 260, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Шт.", Width = 55, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "«Типоразмер УГО» в типе", Width = 220, ReadOnly = true });

            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                HeaderText = "УГО схемы",
                Width = 300,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                Sorted = false
            });

            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Откуда", Width = 130, ReadOnly = true });

            _grid.CellValueChanged += OnCellChanged;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Без этого выбор в списке доходит до обработчика только после ухода из ячейки,
                // и подпись «Откуда» отстаёт на один клик.
                if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            _summary.Dock = DockStyle.Bottom;
            _summary.Height = 24;
            _summary.Padding = new Padding(14, 4, 14, 0);

            var ok = new Button { Text = "Применить", Size = new Size(130, 28), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Отмена", Size = new Size(110, 28), DialogResult = DialogResult.Cancel };

            var clear = new Button { Text = "Снять все", Size = new Size(120, 28) };
            clear.Click += (s, e) =>
            {
                foreach (UgoMatch match in _matches) { match.Selected = null; match.Guessed = false; }
                Fill();
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 46,
                Padding = new Padding(8, 9, 8, 0)
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(clear);

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 14, 6) };
            body.Controls.Add(_grid);

            Controls.Add(body);
            Controls.Add(hint);
            Controls.Add(_summary);
            Controls.Add(buttons);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Fill()
        {
            if (_candidates.Count == 0) _candidates = UgoFinder.Candidates(_document);
            if (_matches.Count == 0) _matches = UgoFinder.Match(_document, _rows, _candidates, _settings);

            _grid.Rows.Clear();

            var names = new List<string> { NoUgo };
            names.AddRange(_candidates.Select(c => c.ToString()));

            foreach (UgoMatch match in _matches)
            {
                int index = _grid.Rows.Add(
                    match.Row.Code,
                    match.Row.TypeName,
                    match.Row.Count.ToString(CultureInfo.CurrentCulture),
                    match.Declared,
                    null,
                    string.Empty);

                DataGridViewRow row = _grid.Rows[index];
                var cell = (DataGridViewComboBoxCell)row.Cells[ColumnUgo];

                cell.Items.AddRange(names.Cast<object>().ToArray());
                cell.Value = match.Selected == null ? NoUgo : match.Selected.ToString();

                UpdateState(row, match);
            }

            RefreshSummary();
        }

        private void OnCellChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColumnUgo || e.RowIndex >= _matches.Count) return;

            UgoMatch match = _matches[e.RowIndex];
            string value = _grid.Rows[e.RowIndex].Cells[ColumnUgo].Value as string;

            match.Selected = string.IsNullOrEmpty(value) || value == NoUgo
                ? null
                : _candidates.FirstOrDefault(c => c.ToString() == value);

            // Выбор человека перестаёт быть догадкой — и подпись это показывает.
            match.Guessed = false;

            UpdateState(_grid.Rows[e.RowIndex], match);
            RefreshSummary();
        }

        private static void UpdateState(DataGridViewRow row, UgoMatch match)
        {
            if (match.Selected == null)
            {
                row.Cells[ColumnState].Value = "нет — блок текстом";
                row.Cells[ColumnState].Style.ForeColor = Color.Firebrick;
                return;
            }

            row.Cells[ColumnState].Value = match.Guessed ? "подставлено плагином" : "выбрано";
            row.Cells[ColumnState].Style.ForeColor = match.Guessed ? SystemColors.GrayText : Color.DarkGreen;
        }

        private void RefreshSummary()
        {
            int chosen = _matches.Count(m => m.Selected != null);
            int guessed = _matches.Count(m => m.Selected != null && m.Guessed);

            _summary.Text = "Типоразмеров: " + _matches.Count.ToString(CultureInfo.CurrentCulture) +
                            ", с УГО: " + chosen.ToString(CultureInfo.CurrentCulture) +
                            " (подставлено плагином: " + guessed.ToString(CultureInfo.CurrentCulture) + ")" +
                            ", без УГО: " + (_matches.Count - chosen).ToString(CultureInfo.CurrentCulture) +
                            ".   Аннотаций в проекте: " + _candidates.Count.ToString(CultureInfo.CurrentCulture);
        }
    }
}
