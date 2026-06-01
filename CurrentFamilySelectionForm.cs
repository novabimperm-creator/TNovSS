using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public class CurrentFamilySelectionForm : BaseForm
    {
        public Family SelectedFamily { get; private set; }
        public int SymbolCount { get; private set; }

        private Document _doc;
        private List<Family> _filteredFamilies;

        private System.Windows.Forms.ListView listView;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.Label previewLabel;
        private System.Windows.Forms.Label statsLabel;

        private static readonly BuiltInCategory[] AllowedCategories = new BuiltInCategory[]
        {
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_LightingFixtures
        };

        public CurrentFamilySelectionForm(Document doc) : base()
        {
            _doc = doc;

            InitializeForm();
            LoadFamilies();

            this.ShowBackButton = true;
            this.NextButtonText = "Далее →";

            base.NextClicked += (s, e) => OnNextClick();
            base.BackClicked += (s, e) => { this.DialogResult = DialogResult.Abort; this.Close(); };
            base.CancelClicked += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        }

        private void InitializeForm()
        {
            this.Text = "Выбор семейства электрооборудования";
            this.Size = new System.Drawing.Size(1000, 700);

            // Основной контейнер
            System.Windows.Forms.Panel mainPanel = new System.Windows.Forms.Panel();
            mainPanel.Dock = DockStyle.Fill;
            mainPanel.Padding = new Padding(20);

            // Заголовок
            System.Windows.Forms.Label titleLabel = new System.Windows.Forms.Label();
            titleLabel.Text = "ВЫБОР СЕМЕЙСТВА Б (ЭЛЕКТРО)";
            titleLabel.Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = base.TextColor;
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 40;
            titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Описание
            System.Windows.Forms.Label descLabel = new System.Windows.Forms.Label();
            descLabel.Text = "Выберите семейство электрооборудования для размещения рядом с элементами ОВ/ВК:";
            descLabel.Font = new System.Drawing.Font("Segoe UI", 9);
            descLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            descLabel.Dock = DockStyle.Top;
            descLabel.Height = 30;
            descLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Панель поиска
            System.Windows.Forms.Panel searchPanel = new System.Windows.Forms.Panel();
            searchPanel.Dock = DockStyle.Top;
            searchPanel.Height = 50;
            searchPanel.Padding = new Padding(0, 5, 0, 5);

            System.Windows.Forms.Label searchLabel = new System.Windows.Forms.Label();
            searchLabel.Text = "Поиск:";
            searchLabel.Location = new System.Drawing.Point(0, 15);
            searchLabel.Size = new System.Drawing.Size(50, 25);
            searchLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            searchBox = new System.Windows.Forms.TextBox();
            searchBox.Location = new System.Drawing.Point(55, 12);
            searchBox.Size = new System.Drawing.Size(250, 25);
            searchBox.TextChanged += (s, e) => FilterFamilies();

            System.Windows.Forms.Button clearButton = new System.Windows.Forms.Button();
            clearButton.Text = "Очистить";
            clearButton.Location = new System.Drawing.Point(315, 12);
            clearButton.Size = new System.Drawing.Size(80, 25);
            clearButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            clearButton.ForeColor = System.Drawing.Color.White;
            clearButton.FlatStyle = FlatStyle.Flat;
            clearButton.Click += (s, e) => { searchBox.Text = ""; FilterFamilies(); };

            statsLabel = new System.Windows.Forms.Label();
            statsLabel.Text = "Семейств: 0";
            statsLabel.Location = new System.Drawing.Point(650, 15);
            statsLabel.Size = new System.Drawing.Size(300, 25);
            statsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            searchPanel.Controls.Add(searchLabel);
            searchPanel.Controls.Add(searchBox);
            searchPanel.Controls.Add(clearButton);
            searchPanel.Controls.Add(statsLabel);

            // Список семейств
            listView = new System.Windows.Forms.ListView();
            listView.Dock = DockStyle.Fill;
            listView.View = System.Windows.Forms.View.Details;
            listView.FullRowSelect = true;
            listView.GridLines = true;
            listView.Font = new System.Drawing.Font("Segoe UI", 9);
            listView.Columns.Add("Семейство", 400);
            listView.Columns.Add("Категория", 300);
            listView.Columns.Add("Типов в семействе", 150);
            listView.SelectedIndexChanged += (s, e) => UpdatePreview();
            listView.DoubleClick += (s, e) => OnNextClick();

            // Панель предпросмотра
            System.Windows.Forms.Panel previewPanel = new System.Windows.Forms.Panel();
            previewPanel.Dock = DockStyle.Bottom;
            previewPanel.Height = 40;
            previewPanel.BackColor = System.Drawing.Color.FromArgb(248, 249, 250);
            previewPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            previewLabel = new System.Windows.Forms.Label();
            previewLabel.Text = "Выбрано: ничего";
            previewLabel.Dock = DockStyle.Fill;
            previewLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            previewLabel.Padding = new Padding(10, 0, 0, 0);
            previewLabel.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            previewLabel.ForeColor = base.AccentColor;

            previewPanel.Controls.Add(previewLabel);

            // Добавляем всё на форму
            mainPanel.Controls.Add(listView);
            mainPanel.Controls.Add(previewPanel);
            mainPanel.Controls.Add(searchPanel);
            mainPanel.Controls.Add(descLabel);
            mainPanel.Controls.Add(titleLabel);

            base.ContentPanel.Controls.Add(mainPanel);
        }

        private void LoadFamilies()
        {
            // Получаем семейства только по разрешенным категориям
            _filteredFamilies = new FilteredElementCollector(_doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null && IsCategoryAllowed(f.FamilyCategory.Id))
                .OrderBy(f => f.Name)
                .ToList();

            foreach (var family in _filteredFamilies)
            {
                int symbolCount = family.GetFamilySymbolIds().Count;

                System.Windows.Forms.ListViewItem item = new System.Windows.Forms.ListViewItem(family.Name);
                item.SubItems.Add(family.FamilyCategory?.Name ?? "Неизвестно");
                item.SubItems.Add(symbolCount.ToString());
                item.Tag = family;

                if (symbolCount > 0)
                {
                    item.SubItems[2].ForeColor = System.Drawing.Color.Green;
                }

                listView.Items.Add(item);
            }

            UpdateStats();

            if (listView.Items.Count > 0)
            {
                listView.Items[0].Selected = true;
            }
        }

        private bool IsCategoryAllowed(ElementId categoryId)
        {
            foreach (var category in AllowedCategories)
            {
                if (new ElementId(category) == categoryId)
                    return true;
            }
            return false;
        }

        private void FilterFamilies()
        {
            string search = searchBox.Text.Trim().ToLower();

            listView.Items.Clear();
            listView.BeginUpdate();

            var filtered = _filteredFamilies.Where(f =>
                f.Name.ToLower().Contains(search) ||
                (f.FamilyCategory?.Name ?? "").ToLower().Contains(search));

            foreach (var family in filtered)
            {
                int symbolCount = family.GetFamilySymbolIds().Count;

                System.Windows.Forms.ListViewItem item = new System.Windows.Forms.ListViewItem(family.Name);
                item.SubItems.Add(family.FamilyCategory?.Name ?? "Неизвестно");
                item.SubItems.Add(symbolCount.ToString());
                item.Tag = family;

                if (symbolCount > 0)
                {
                    item.SubItems[2].ForeColor = System.Drawing.Color.Green;
                }

                listView.Items.Add(item);
            }

            listView.EndUpdate();
            UpdateStats();

            if (listView.Items.Count > 0 && listView.SelectedItems.Count == 0)
            {
                listView.Items[0].Selected = true;
            }
        }

        private void UpdateStats()
        {
            string search = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(search))
            {
                statsLabel.Text = $"Семейств электрооборудования: {listView.Items.Count}";
            }
            else
            {
                statsLabel.Text = $"Найдено: {listView.Items.Count}";
            }
        }

        private void UpdatePreview()
        {
            if (listView.SelectedItems.Count > 0)
            {
                var item = listView.SelectedItems[0];
                var family = item.Tag as Family;
                var count = item.SubItems[2].Text;

                previewLabel.Text = $"Выбрано: {family.Name} - {count} тип(ов) в семействе";
                SelectedFamily = family;
                SymbolCount = int.Parse(count);
                base.EnableNextButton(true);
            }
            else
            {
                previewLabel.Text = "Выбрано: ничего";
                SelectedFamily = null;
                SymbolCount = 0;
                base.EnableNextButton(false);
            }
        }

        private void OnNextClick()
        {
            if (SelectedFamily != null)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                System.Windows.Forms.MessageBox.Show("Пожалуйста, выберите семейство", "Внимание",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }
    }
}