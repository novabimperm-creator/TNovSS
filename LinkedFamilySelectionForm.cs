using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class LinkedFamilySelectionForm : System.Windows.Forms.Form
    {
        public Autodesk.Revit.DB.Family SelectedFamily { get; private set; }
        public int InstanceCountOnLevel { get; private set; }

        private List<Autodesk.Revit.DB.Family> _filteredFamilies;
        private Autodesk.Revit.DB.Document _linkDoc;
        private Level _selectedLevel;
        private System.Windows.Forms.ListView _familyListView;
        private System.Windows.Forms.TextBox _searchTextBox;
        private System.Windows.Forms.Label _levelInfoLabel;
        private System.Windows.Forms.Label _statsLabel;

        // Категории для семейства А (ОВ/ВК + ЭЛ электрооборудование)
        private static readonly BuiltInCategory[] _allowedCategoriesA = new BuiltInCategory[]
        {
            // ОВ/ВК категории (6 категорий)
            BuiltInCategory.OST_DuctAccessory,      // Арматура воздуховодов
            BuiltInCategory.OST_PipeAccessory,      // Арматура трубопроводов
            BuiltInCategory.OST_DuctTerminal,       // Воздухораспределители
            BuiltInCategory.OST_MechanicalEquipment, // Оборудование
            BuiltInCategory.OST_DuctFitting,        // Соединительные детали воздуховодов
            BuiltInCategory.OST_PipeFitting,        // Соединительные детали трубопроводов
            
            // ЭЛ категории (добавлена электрооборудование)
            BuiltInCategory.OST_ElectricalEquipment // Электрооборудование (для файлов ЭЛ)
        };

        public LinkedFamilySelectionForm(Autodesk.Revit.DB.Document linkDoc, Level selectedLevel)
        {
            _linkDoc = linkDoc;
            _selectedLevel = selectedLevel;
            SelectedFamily = null;
            InstanceCountOnLevel = 0;

            // Фильтруем семейства только по разрешенным категориям
            _filteredFamilies = new Autodesk.Revit.DB.FilteredElementCollector(linkDoc)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .Cast<Autodesk.Revit.DB.Family>()
                .Where(f => f.FamilyCategory != null &&
                           IsCategoryAllowed(f.FamilyCategory.Id))
                .OrderBy(f => f.Name)
                .ToList();

            InitializeForm();
            LoadFamilies();
        }

        private bool IsCategoryAllowed(ElementId categoryId)
        {
            foreach (var allowedCategory in _allowedCategoriesA)
            {
                if (new ElementId(allowedCategory) == categoryId)
                    return true;
            }
            return false;
        }

        private string GetCategoryName(BuiltInCategory category)
        {
            return LabelUtils.GetLabelFor(category);
        }

        private void InitializeForm()
        {
            // Основные настройки формы
            this.Text = "Выбор семейства из связанного файла";
            this.Size = new System.Drawing.Size(1000, 700);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(240, 245, 255);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(800, 600);
            this.MaximizeBox = true;

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label();
            titleLabel.Text = "ВЫБОР СЕМЕЙСТВА А (ОВ/ВК/ЭЛ)";
            titleLabel.Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = System.Drawing.Color.FromArgb(0, 80, 160);
            titleLabel.Location = new System.Drawing.Point(20, 20);
            titleLabel.Size = new System.Drawing.Size(950, 30);
            titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // Информация об уровне
            _levelInfoLabel = new System.Windows.Forms.Label();
            _levelInfoLabel.Text = $"Выбранный этаж: {(_selectedLevel?.Name ?? "Не определен")}";
            _levelInfoLabel.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            _levelInfoLabel.ForeColor = System.Drawing.Color.FromArgb(0, 80, 160);
            _levelInfoLabel.Location = new System.Drawing.Point(20, 55);
            _levelInfoLabel.Size = new System.Drawing.Size(950, 20);
            _levelInfoLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Описание
            var descLabel = new System.Windows.Forms.Label();
            descLabel.Text = "Выберите семейство ОВ/ВК или электрооборудования (Семейство А) для размещения рядом с ним электрооборудования:";
            descLabel.Font = new System.Drawing.Font("Segoe UI", 10);
            descLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            descLabel.Location = new System.Drawing.Point(20, 80);
            descLabel.Size = new System.Drawing.Size(950, 25);
            descLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Информация о типах файлов
            var fileTypeLabel = new System.Windows.Forms.Label();
            fileTypeLabel.Text = "Поддерживаемые категории: ОВ/ВК (6 категорий) + ЭЛ (Электрооборудование)";
            fileTypeLabel.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Italic);
            fileTypeLabel.ForeColor = System.Drawing.Color.FromArgb(120, 120, 120);
            fileTypeLabel.Location = new System.Drawing.Point(20, 105);
            fileTypeLabel.Size = new System.Drawing.Size(950, 20);
            fileTypeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Панель поиска
            var searchPanel = new System.Windows.Forms.Panel();
            searchPanel.Location = new System.Drawing.Point(20, 130);
            searchPanel.Size = new System.Drawing.Size(950, 35);
            searchPanel.BackColor = System.Drawing.Color.White;
            searchPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            var searchLabel = new System.Windows.Forms.Label();
            searchLabel.Text = "Поиск:";
            searchLabel.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            searchLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            searchLabel.Location = new System.Drawing.Point(10, 8);
            searchLabel.Size = new System.Drawing.Size(50, 20);
            searchLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            _searchTextBox = new System.Windows.Forms.TextBox();
            _searchTextBox.Location = new System.Drawing.Point(65, 6);
            _searchTextBox.Size = new System.Drawing.Size(300, 23);
            _searchTextBox.Font = new System.Drawing.Font("Segoe UI", 9);
            _searchTextBox.TextChanged += (s, e) => FilterFamilies();

            var searchButton = new System.Windows.Forms.Button();
            searchButton.Text = "Найти";
            searchButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            searchButton.Location = new System.Drawing.Point(370, 5);
            searchButton.Size = new System.Drawing.Size(60, 25);
            searchButton.BackColor = System.Drawing.Color.FromArgb(0, 123, 255);
            searchButton.ForeColor = System.Drawing.Color.White;
            searchButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            searchButton.Click += (s, e) => FilterFamilies();

            var clearSearchButton = new System.Windows.Forms.Button();
            clearSearchButton.Text = "Очистить";
            clearSearchButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            clearSearchButton.Location = new System.Drawing.Point(435, 5);
            clearSearchButton.Size = new System.Drawing.Size(70, 25);
            clearSearchButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            clearSearchButton.ForeColor = System.Drawing.Color.White;
            clearSearchButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            clearSearchButton.Click += (s, e) =>
            {
                _searchTextBox.Text = "";
                FilterFamilies();
            };

            // Статистика
            _statsLabel = new System.Windows.Forms.Label();
            _statsLabel.Text = $"Всего семейств: {_filteredFamilies.Count}";
            _statsLabel.Font = new System.Drawing.Font("Segoe UI", 9);
            _statsLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            _statsLabel.Location = new System.Drawing.Point(520, 8);
            _statsLabel.Size = new System.Drawing.Size(420, 20);
            _statsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;

            searchPanel.Controls.AddRange(new System.Windows.Forms.Control[] {
                searchLabel, _searchTextBox, searchButton, clearSearchButton, _statsLabel
            });

            // ListView для семейств
            _familyListView = new System.Windows.Forms.ListView();
            _familyListView.Location = new System.Drawing.Point(20, 180);
            _familyListView.Size = new System.Drawing.Size(950, 450);
            _familyListView.View = System.Windows.Forms.View.Details;
            _familyListView.FullRowSelect = true;
            _familyListView.GridLines = true;
            _familyListView.Font = new System.Drawing.Font("Segoe UI", 9);
            _familyListView.BackColor = System.Drawing.Color.White;
            _familyListView.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            _familyListView.MultiSelect = false;

            // Колонки
            _familyListView.Columns.Add("Семейство", 400);
            _familyListView.Columns.Add("Категория", 300);
            _familyListView.Columns.Add("На этаже", 150);

            // Сортировка по колонкам
            _familyListView.ColumnClick += (s, e) => SortListView(e.Column);

            // Панель предпросмотра
            var previewPanel = new System.Windows.Forms.Panel();
            previewPanel.Location = new System.Drawing.Point(20, 640);
            previewPanel.Size = new System.Drawing.Size(950, 25);
            previewPanel.BackColor = System.Drawing.Color.FromArgb(230, 240, 255);
            previewPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            var previewLabel = new System.Windows.Forms.Label();
            previewLabel.Text = "Выбрано: ничего";
            previewLabel.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            previewLabel.ForeColor = System.Drawing.Color.FromArgb(0, 80, 160);
            previewLabel.Location = new System.Drawing.Point(5, 3);
            previewLabel.Size = new System.Drawing.Size(940, 18);
            previewLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            previewLabel.Name = "previewLabel";
            previewPanel.Controls.Add(previewLabel);

            // Кнопка выбора
            var selectButton = new System.Windows.Forms.Button();
            selectButton.Text = "ВЫБРАТЬ СЕМЕЙСТВО А";
            selectButton.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            selectButton.Location = new System.Drawing.Point(300, 675);
            selectButton.Size = new System.Drawing.Size(250, 35);
            selectButton.BackColor = System.Drawing.Color.FromArgb(0, 123, 255);
            selectButton.ForeColor = System.Drawing.Color.White;
            selectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            selectButton.Click += (s, e) =>
            {
                if (SelectedFamily != null)
                {
                    this.DialogResult = System.Windows.Forms.DialogResult.OK;
                    this.Close();
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("Пожалуйста, выберите семейство", "Внимание",
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                }
            };

            // Кнопка отмены
            var cancelButton = new System.Windows.Forms.Button();
            cancelButton.Text = "ОТМЕНА";
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            cancelButton.Location = new System.Drawing.Point(570, 675);
            cancelButton.Size = new System.Drawing.Size(100, 35);
            cancelButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            cancelButton.ForeColor = System.Drawing.Color.White;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;

            // Добавляем элементы
            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                titleLabel, _levelInfoLabel, descLabel, fileTypeLabel, searchPanel,
                _familyListView, previewPanel, selectButton, cancelButton
            });

            // События
            _familyListView.SelectedIndexChanged += (s, e) => UpdatePreview();
            _familyListView.DoubleClick += (s, e) =>
            {
                if (SelectedFamily != null)
                {
                    this.DialogResult = System.Windows.Forms.DialogResult.OK;
                    this.Close();
                }
            };
            _searchTextBox.KeyDown += (s, e) => { if (e.KeyCode == System.Windows.Forms.Keys.Enter) FilterFamilies(); };

            // Назначаем кнопки
            this.AcceptButton = selectButton;
            this.CancelButton = cancelButton;
        }

        private void LoadFamilies()
        {
            foreach (var family in _filteredFamilies)
            {
                // Считаем количество экземпляров на выбранном этаже
                int instanceCount = GetInstanceCountOnLevel(family);

                var listItem = new System.Windows.Forms.ListViewItem(new[] {
                    family.Name,
                    family.FamilyCategory?.Name ?? "Неизвестно",
                    instanceCount.ToString()
                });
                listItem.Tag = family;

                // Подсветка электрооборудования (категория ЭЛ)
                if (family.FamilyCategory != null)
                {
#if R2022
                        long catId = family.FamilyCategory.Id.IntegerValue;
#else
                    long catId = family.FamilyCategory.Id.Value;
#endif
                    if (catId == (int)BuiltInCategory.OST_ElectricalEquipment)
                    {
                        listItem.ForeColor = System.Drawing.Color.FromArgb(220, 53, 69); // Красный для ЭЛ
                        listItem.Font = new System.Drawing.Font(_familyListView.Font, System.Drawing.FontStyle.Bold);
                    }
                }
                // Подсветка ОВ/ВК категорий
                else if (family.FamilyCategory != null) 
                {
#if R2022
                        long catId = family.FamilyCategory.Id.IntegerValue;
#else
                    long catId = family.FamilyCategory.Id.Value;
#endif
                    if (catId == (int)BuiltInCategory.OST_MechanicalEquipment ||
                     catId == (int)BuiltInCategory.OST_DuctAccessory ||
                     catId == (int)BuiltInCategory.OST_PipeAccessory)
                    {
                        listItem.ForeColor = System.Drawing.Color.FromArgb(0, 123, 255); // Синий для ОВ/ВК
                    } 
                }

                _familyListView.Items.Add(listItem);
            }

            UpdateStats();

            // Выбираем первый элемент
            if (_familyListView.Items.Count > 0)
            {
                _familyListView.Items[0].Selected = true;
                UpdatePreview();
            }
        }

        private int GetInstanceCountOnLevel(Family family)
        {
            try
            {
                // Получаем все экземпляры этого семейства на выбранном уровне
                var instances = new FilteredElementCollector(_linkDoc)
                    .OfClass(typeof(FamilyInstance))
                    .Where(x => ((FamilyInstance)x).Symbol.Family.Id == family.Id)
                    .Cast<FamilyInstance>();

                if (_selectedLevel != null)
                {
                    instances = instances.Where(i => i.LevelId == _selectedLevel.Id);
                }

                return instances.Count();
            }
            catch
            {
                return 0;
            }
        }

        private void FilterFamilies()
        {
            string searchText = _searchTextBox.Text.Trim().ToLower();

            _familyListView.Items.Clear();
            _familyListView.BeginUpdate();

            var filtered = _filteredFamilies.Where(family =>
                family.Name.ToLower().Contains(searchText) ||
                (family.FamilyCategory?.Name ?? "").ToLower().Contains(searchText));

            foreach (var family in filtered)
            {
                int instanceCount = GetInstanceCountOnLevel(family);

                var listItem = new System.Windows.Forms.ListViewItem(new[] {
                    family.Name,
                    family.FamilyCategory?.Name ?? "Неизвестно",
                    instanceCount.ToString()
                });
                listItem.Tag = family;

                // Подсветка электрооборудования (категория ЭЛ)
                if (family.FamilyCategory != null)
                {
#if R2022
                        long catId = family.FamilyCategory.Id.IntegerValue;
#else
                    long catId = family.FamilyCategory.Id.Value;
#endif
                    if (catId == (int)BuiltInCategory.OST_ElectricalEquipment)
                    {
                        listItem.ForeColor = System.Drawing.Color.FromArgb(220, 53, 69); // Красный для ЭЛ
                        listItem.Font = new System.Drawing.Font(_familyListView.Font, System.Drawing.FontStyle.Bold);
                    }
                }
                // Подсветка ОВ/ВК категорий
                else if (family.FamilyCategory != null)
                {
#if R2022
                        long catId = family.FamilyCategory.Id.IntegerValue;
#else
                    long catId = family.FamilyCategory.Id.Value;
#endif
                    if (catId == (int)BuiltInCategory.OST_MechanicalEquipment ||
                         catId == (int)BuiltInCategory.OST_DuctAccessory ||
                         catId == (int)BuiltInCategory.OST_PipeAccessory)
                    {
                        listItem.ForeColor = System.Drawing.Color.FromArgb(0, 123, 255); // Синий для ОВ/ВК
                    }
                }
                _familyListView.Items.Add(listItem);
            }

            _familyListView.EndUpdate();
            UpdateStats();
            UpdatePreview();
        }

        private void UpdateStats()
        {
            string searchText = _searchTextBox.Text.Trim();
            if (string.IsNullOrEmpty(searchText))
            {
                _statsLabel.Text = $"Семейств ОВ/ВК/ЭЛ: {_familyListView.Items.Count}";
            }
            else
            {
                _statsLabel.Text = $"Найдено: {_familyListView.Items.Count}";
            }
        }

        private void SortListView(int columnIndex)
        {
            if (_familyListView.Items.Count == 0) return;

            _familyListView.ListViewItemSorter = new ListViewItemComparer(columnIndex);
            _familyListView.Sort();
        }

        private void UpdatePreview()
        {
            if (_familyListView.SelectedItems.Count > 0)
            {
                var selectedItem = _familyListView.SelectedItems[0];
                var familyName = selectedItem.SubItems[0].Text;
                var category = selectedItem.SubItems[1].Text;
                var count = selectedItem.SubItems[2].Text;

                var previewLabel = this.Controls.Find("previewLabel", true).FirstOrDefault() as System.Windows.Forms.Label;
                if (previewLabel != null)
                {
                    previewLabel.Text = $"Выбрано: {familyName} ({category}) - {count} шт. на этаже";
                    SelectedFamily = selectedItem.Tag as Autodesk.Revit.DB.Family;
                    InstanceCountOnLevel = int.Parse(count);
                }
            }
            else
            {
                var previewLabel = this.Controls.Find("previewLabel", true).FirstOrDefault() as System.Windows.Forms.Label;
                if (previewLabel != null)
                {
                    previewLabel.Text = "Выбрано: ничего";
                    SelectedFamily = null;
                    InstanceCountOnLevel = 0;
                }
            }
        }

        // Класс для сортировки ListView
        private class ListViewItemComparer : System.Collections.IComparer
        {
            private int _columnIndex;

            public ListViewItemComparer(int columnIndex)
            {
                _columnIndex = columnIndex;
            }

            public int Compare(object x, object y)
            {
                System.Windows.Forms.ListViewItem itemX = (System.Windows.Forms.ListViewItem)x;
                System.Windows.Forms.ListViewItem itemY = (System.Windows.Forms.ListViewItem)y;

                string textX = itemX.SubItems[_columnIndex].Text;
                string textY = itemY.SubItems[_columnIndex].Text;

                // Для колонки "На этаже" сортируем как числа
                if (_columnIndex == 2 && int.TryParse(textX, out int numX) && int.TryParse(textY, out int numY))
                {
                    return numX.CompareTo(numY);
                }

                // Остальные колонки сортируем как строки
                return string.Compare(textX, textY, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}