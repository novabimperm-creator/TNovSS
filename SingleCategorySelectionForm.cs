using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Autodesk.Revit.DB;

namespace TNovSS
{
    public partial class SingleCategorySelectionForm : System.Windows.Forms.Form
    {
        private BuiltInCategory _selectedCategory = BuiltInCategory.INVALID;
        private List<BuiltInCategory> _availableCategories;

        public SingleCategorySelectionForm(Document doc)
        {
            _availableCategories = GetAvailableCategories();
            InitializeForm();
        }

        private void InitializeForm()
        {
            this.Text = "Pikachu Plugin - Выбор категории";
            this.Size = new System.Drawing.Size(450, 550);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(240, 245, 255);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;

            // Заголовок формы
            var titleLabel = new System.Windows.Forms.Label();
            titleLabel.Text = "⚡ ВЫДЕЛИТЬ ВСЕ ЭЛЕМЕНТЫ КАТЕГОРИИ";
            titleLabel.Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = System.Drawing.Color.FromArgb(0, 80, 160);
            titleLabel.Location = new System.Drawing.Point(20, 20);
            titleLabel.Size = new System.Drawing.Size(400, 30);
            titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // Описание
            var descLabel = new System.Windows.Forms.Label();
            descLabel.Text = "Выберите категорию для выделения всех элементов:\nГод напряженный - работаем быстро и точно! 🚀";
            descLabel.Font = new System.Drawing.Font("Segoe UI", 9);
            descLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            descLabel.Location = new System.Drawing.Point(20, 60);
            descLabel.Size = new System.Drawing.Size(400, 40);
            descLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // ListBox для выбора категории
            var categoryListBox = new System.Windows.Forms.ListBox();
            categoryListBox.Location = new System.Drawing.Point(20, 110);
            categoryListBox.Size = new System.Drawing.Size(400, 300);
            categoryListBox.Font = new System.Drawing.Font("Segoe UI", 10);
            categoryListBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            categoryListBox.SelectionMode = System.Windows.Forms.SelectionMode.One;

            // Заполняем список категорий
            foreach (var category in _availableCategories)
            {
                string categoryName = LabelUtils.GetLabelFor(category);
                categoryListBox.Items.Add(categoryName);
            }

            // Автоматически выбираем первый элемент
            if (categoryListBox.Items.Count > 0)
            {
                categoryListBox.SelectedIndex = 0;
            }

            // Метка для количества элементов
            var countLabel = new System.Windows.Forms.Label();
            countLabel.Text = "Элементов в категории: 0";
            countLabel.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Italic);
            countLabel.ForeColor = System.Drawing.Color.FromArgb(0, 123, 255);
            countLabel.Location = new System.Drawing.Point(20, 420);
            countLabel.Size = new System.Drawing.Size(200, 20);

            // Обновляем метку при выборе категории
            categoryListBox.SelectedIndexChanged += (s, e) =>
            {
                if (categoryListBox.SelectedIndex >= 0)
                {
                    countLabel.Text = $"Выбрана категория: {categoryListBox.SelectedItem}";
                }
            };

            // Кнопка выделить
            var selectButton = new System.Windows.Forms.Button();
            selectButton.Text = "⚡ ВЫДЕЛИТЬ ЭЛЕМЕНТЫ";
            selectButton.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            selectButton.Location = new System.Drawing.Point(100, 450);
            selectButton.Size = new System.Drawing.Size(150, 40);
            selectButton.BackColor = System.Drawing.Color.FromArgb(255, 193, 7); // Желтый цвет Пикачу
            selectButton.ForeColor = System.Drawing.Color.Black;
            selectButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            selectButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            selectButton.Click += (s, e) =>
            {
                if (categoryListBox.SelectedIndex >= 0)
                {
                    _selectedCategory = _availableCategories[categoryListBox.SelectedIndex];
                }
            };

            // Кнопка отмены
            var cancelButton = new System.Windows.Forms.Button();
            cancelButton.Text = "ОТМЕНА";
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            cancelButton.Location = new System.Drawing.Point(260, 450);
            cancelButton.Size = new System.Drawing.Size(100, 40);
            cancelButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            cancelButton.ForeColor = System.Drawing.Color.White;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;

            // Добавляем элементы на форму
            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                titleLabel, descLabel, categoryListBox,
                countLabel, selectButton, cancelButton
            });

            this.AcceptButton = selectButton;
            this.CancelButton = cancelButton;
        }

        public BuiltInCategory GetSelectedCategory()
        {
            return _selectedCategory;
        }

        private List<BuiltInCategory> GetAvailableCategories()
        {
            // Основные категории Revit для выделения
            return new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_Furniture,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Ramps,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Site,
                BuiltInCategory.OST_Parking,
                BuiltInCategory.OST_Planting,
                BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_Grids
            }.OrderBy(cat => LabelUtils.GetLabelFor(cat)).ToList();
        }
    }
}