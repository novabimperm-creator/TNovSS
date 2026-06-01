using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Autodesk.Revit.DB;

namespace TNovSS
{
    public partial class CategorySelectionForm : System.Windows.Forms.Form
    {
        private Dictionary<BuiltInCategory, bool> _categoryStates;
        private List<BuiltInCategory> _allCategories;
        private CheckedListBox _categoryListBox;

        public CategorySelectionForm(Document doc)
        {
            _allCategories = GetRelevantCategories();
            _categoryStates = _allCategories.ToDictionary(c => c, c => true);

            InitializeForm();
        }

        private void InitializeForm()
        {
            this.Text = "Выбор категорий для проверки пересечений";
            this.Size = new System.Drawing.Size(500, 700);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = System.Drawing.Color.FromArgb(240, 245, 255);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label();
            titleLabel.Text = "ВЫБЕРИТЕ КАТЕГОРИИ ДЛЯ ПРОВЕРКИ ПЕРЕСЕЧЕНИЙ";
            titleLabel.Font = new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = System.Drawing.Color.FromArgb(0, 80, 160);
            titleLabel.Location = new System.Drawing.Point(20, 15);
            titleLabel.Size = new System.Drawing.Size(460, 25);
            titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // Описание
            var descLabel = new System.Windows.Forms.Label();
            descLabel.Text = "Выберите категории для проверки коллизий между системами:";
            descLabel.Font = new System.Drawing.Font("Segoe UI", 9);
            descLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            descLabel.Location = new System.Drawing.Point(20, 45);
            descLabel.Size = new System.Drawing.Size(460, 30);
            descLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // CheckedListBox для категорий
            _categoryListBox = new System.Windows.Forms.CheckedListBox();
            _categoryListBox.Location = new System.Drawing.Point(20, 80);
            _categoryListBox.Size = new System.Drawing.Size(460, 400);
            _categoryListBox.Font = new System.Drawing.Font("Segoe UI", 9);
            _categoryListBox.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            _categoryListBox.CheckOnClick = true;

            // Заполняем список категорий с группировкой
            PopulateCategoriesWithGroups();

            // Кнопки выбора всех/очистки
            var selectAllButton = new System.Windows.Forms.Button();
            selectAllButton.Text = "ВЫБРАТЬ ВСЕ";
            selectAllButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            selectAllButton.Location = new System.Drawing.Point(20, 490);
            selectAllButton.Size = new System.Drawing.Size(100, 25);
            selectAllButton.BackColor = System.Drawing.Color.FromArgb(0, 123, 255);
            selectAllButton.ForeColor = System.Drawing.Color.White;
            selectAllButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            selectAllButton.Click += (s, e) =>
            {
                for (int i = 0; i < _categoryListBox.Items.Count; i++)
                    _categoryListBox.SetItemChecked(i, true);
            };

            var clearAllButton = new System.Windows.Forms.Button();
            clearAllButton.Text = "ОЧИСТИТЬ";
            clearAllButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            clearAllButton.Location = new System.Drawing.Point(130, 490);
            clearAllButton.Size = new System.Drawing.Size(100, 25);
            clearAllButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            clearAllButton.ForeColor = System.Drawing.Color.White;
            clearAllButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            clearAllButton.Click += (s, e) =>
            {
                for (int i = 0; i < _categoryListBox.Items.Count; i++)
                    _categoryListBox.SetItemChecked(i, false);
            };

            // Группы систем
            var archButton = new System.Windows.Forms.Button();
            archButton.Text = "АРХИТЕКТУРА";
            archButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            archButton.Location = new System.Drawing.Point(240, 490);
            archButton.Size = new System.Drawing.Size(110, 25);
            archButton.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
            archButton.ForeColor = System.Drawing.Color.White;
            archButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            archButton.Click += (s, e) => SelectSystemCategories("АРХИТЕКТУРА");

            var ovButton = new System.Windows.Forms.Button();
            ovButton.Text = "ОВ";
            ovButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            ovButton.Location = new System.Drawing.Point(360, 490);
            ovButton.Size = new System.Drawing.Size(40, 25);
            ovButton.BackColor = System.Drawing.Color.FromArgb(255, 193, 7);
            ovButton.ForeColor = System.Drawing.Color.Black;
            ovButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            ovButton.Click += (s, e) => SelectSystemCategories("ОВ");

            var vkButton = new System.Windows.Forms.Button();
            vkButton.Text = "ВК";
            vkButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            vkButton.Location = new System.Drawing.Point(240, 520);
            vkButton.Size = new System.Drawing.Size(40, 25);
            vkButton.BackColor = System.Drawing.Color.FromArgb(23, 162, 184);
            vkButton.ForeColor = System.Drawing.Color.White;
            vkButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            vkButton.Click += (s, e) => SelectSystemCategories("ВК");

            var elButton = new System.Windows.Forms.Button();
            elButton.Text = "ЭЛ";
            elButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            elButton.Location = new System.Drawing.Point(290, 520);
            elButton.Size = new System.Drawing.Size(40, 25);
            elButton.BackColor = System.Drawing.Color.FromArgb(220, 53, 69);
            elButton.ForeColor = System.Drawing.Color.White;
            elButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            elButton.Click += (s, e) => SelectSystemCategories("ЭЛ");

            var cableButton = new System.Windows.Forms.Button();
            cableButton.Text = "КАБЕЛИ";
            cableButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            cableButton.Location = new System.Drawing.Point(340, 520);
            cableButton.Size = new System.Drawing.Size(60, 25);
            cableButton.BackColor = System.Drawing.Color.FromArgb(111, 66, 193);
            cableButton.ForeColor = System.Drawing.Color.White;
            cableButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cableButton.Click += (s, e) => SelectSystemCategories("КАБЕЛИ");

            var konstruButton = new System.Windows.Forms.Button();
            konstruButton.Text = "КОНСТР";
            konstruButton.Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold);
            konstruButton.Location = new System.Drawing.Point(410, 520);
            konstruButton.Size = new System.Drawing.Size(70, 25);
            konstruButton.BackColor = System.Drawing.Color.FromArgb(128, 128, 128);
            konstruButton.ForeColor = System.Drawing.Color.White;
            konstruButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            konstruButton.Click += (s, e) => SelectSystemCategories("КОНСТРУКЦИИ");

            // Кнопка подтверждения
            var okButton = new System.Windows.Forms.Button();
            okButton.Text = "НАЧАТЬ ПРОВЕРКУ";
            okButton.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            okButton.Location = new System.Drawing.Point(150, 560);
            okButton.Size = new System.Drawing.Size(150, 35);
            okButton.BackColor = System.Drawing.Color.FromArgb(40, 167, 69);
            okButton.ForeColor = System.Drawing.Color.White;
            okButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;

            // Кнопка отмены
            var cancelButton = new System.Windows.Forms.Button();
            cancelButton.Text = "ОТМЕНА";
            cancelButton.Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold);
            cancelButton.Location = new System.Drawing.Point(310, 560);
            cancelButton.Size = new System.Drawing.Size(80, 35);
            cancelButton.BackColor = System.Drawing.Color.FromArgb(108, 117, 125);
            cancelButton.ForeColor = System.Drawing.Color.White;
            cancelButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;

            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                titleLabel, descLabel, _categoryListBox, selectAllButton,
                clearAllButton, archButton, ovButton, vkButton, elButton,
                cableButton, konstruButton, okButton, cancelButton
            });

            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            // Сохраняем состояние при закрытии
            this.FormClosing += (s, e) =>
            {
                if (this.DialogResult == System.Windows.Forms.DialogResult.OK)
                {
                    for (int i = 0; i < _categoryListBox.Items.Count; i++)
                    {
                        var categoryItem = _categoryListBox.Items[i] as CategoryItem;
                        if (categoryItem != null)
                        {
                            _categoryStates[categoryItem.Category] = _categoryListBox.GetItemChecked(i);
                        }
                    }
                }
            };
        }

        private void PopulateCategoriesWithGroups()
        {
            // АРХИТЕКТУРА (АР)
            _categoryListBox.Items.Add("--- АРХИТЕКТУРА (АР) ---", false);
            AddCategory(BuiltInCategory.OST_Walls, "Стены");
            AddCategory(BuiltInCategory.OST_Doors, "Двери");
            AddCategory(BuiltInCategory.OST_Windows, "Окна");
            AddCategory(BuiltInCategory.OST_Stairs, "Лестницы");
            AddCategory(BuiltInCategory.OST_Rooms, "Помещения");
            AddCategory(BuiltInCategory.OST_Floors, "Перекрытия");
            AddCategory(BuiltInCategory.OST_Roofs, "Крыши");
            AddCategory(BuiltInCategory.OST_Ceilings, "Потолки");
            AddCategory(BuiltInCategory.OST_Ramps, "Пандусы");
            AddCategory(BuiltInCategory.OST_Railings, "Ограждения");
            AddCategory(BuiltInCategory.OST_Furniture, "Мебель");

            // ОВ (ОТОПЛЕНИЕ, ВЕНТИЛЯЦИЯ)
            _categoryListBox.Items.Add("--- ОВ (ОТОПЛЕНИЕ, ВЕНТИЛЯЦИЯ) ---", false);
            AddCategory(BuiltInCategory.OST_DuctCurves, "Воздуховоды");
            AddCategory(BuiltInCategory.OST_DuctFitting, "Детали воздуховодов");
            AddCategory(BuiltInCategory.OST_DuctAccessory, "Арматура воздуховодов");
            AddCategory(BuiltInCategory.OST_DuctTerminal, "Воздухораспределители");
            AddCategory(BuiltInCategory.OST_MechanicalEquipment, "Оборудование ОВ");
            AddCategory(BuiltInCategory.OST_PipeCurves, "Трубы ОВ");

            // ВК (ВОДОСНАБЖЕНИЕ И КАНАЛИЗАЦИЯ)
            _categoryListBox.Items.Add("--- ВК (ВОДОСНАБЖЕНИЕ И КАНАЛИЗАЦИЯ) ---", false);
            AddCategory(BuiltInCategory.OST_PipeCurves, "Трубы ВК");
            AddCategory(BuiltInCategory.OST_PipeFitting, "Детали труб");
            AddCategory(BuiltInCategory.OST_PipeAccessory, "Арматура трубопроводов");
            AddCategory(BuiltInCategory.OST_PlumbingFixtures, "Сантехнические приборы");
            AddCategory(BuiltInCategory.OST_MechanicalEquipment, "Оборудование ВК");

            // ЭЛ (ЭЛЕКТРИКА)
            _categoryListBox.Items.Add("--- ЭЛ (ЭЛЕКТРИКА) ---", false);
            AddCategory(BuiltInCategory.OST_ElectricalEquipment, "Электрооборудование");
            AddCategory(BuiltInCategory.OST_CommunicationDevices, "Устройства связи");
            AddCategory(BuiltInCategory.OST_NurseCallDevices, "Устройства вызова и оповещения");
            AddCategory(BuiltInCategory.OST_FireAlarmDevices, "Пожарная сигнализация");
            AddCategory(BuiltInCategory.OST_SecurityDevices, "Датчики безопасности");
            AddCategory(BuiltInCategory.OST_LightingFixtures, "Светильники");
            AddCategory(BuiltInCategory.OST_ElectricalFixtures, "Электроарматура");

            // КАБЕЛЬНЫЕ СИСТЕМЫ (НОВЫЕ КАТЕГОРИИ)
            _categoryListBox.Items.Add("--- КАБЕЛЬНЫЕ СИСТЕМЫ ---", false);
            AddCategory(BuiltInCategory.OST_CableTray, "Кабельные лотки");
            AddCategory(BuiltInCategory.OST_CableTrayFitting, "Соединительные детали кабельных лотков");
            AddCategory(BuiltInCategory.OST_Conduit, "Кабельные каналы");
            AddCategory(BuiltInCategory.OST_ConduitFitting, "Соединительные детали каналов");

            // КОНСТРУКЦИИ
            _categoryListBox.Items.Add("--- КОНСТРУКЦИИ ---", false);
            AddCategory(BuiltInCategory.OST_StructuralFraming, "Конструкции");
            AddCategory(BuiltInCategory.OST_StructuralColumns, "Конструкционные колонны");
            AddCategory(BuiltInCategory.OST_Columns, "Колонны");
            AddCategory(BuiltInCategory.OST_StructuralFoundation, "Фундаменты"); // ИСПРАВЛЕНО!

            // ОБЩИЕ КАТЕГОРИИ
            _categoryListBox.Items.Add("--- ОБЩИЕ КАТЕГОРИИ ---", false);
            AddCategory(BuiltInCategory.OST_SpecialityEquipment, "Спецоборудование");
            AddCategory(BuiltInCategory.OST_GenericModel, "Обобщенные модели");
        }

        private void AddCategory(BuiltInCategory category, string displayName)
        {
            var categoryItem = new CategoryItem(category, displayName);
            _categoryListBox.Items.Add(categoryItem, true);
        }

        private void SelectSystemCategories(string system)
        {
            for (int i = 0; i < _categoryListBox.Items.Count; i++)
            {
                var item = _categoryListBox.Items[i];
                if (item is string header)
                {
                    if (header.Contains(system))
                    {
                        // Нашли заголовок системы, выбираем все категории до следующего заголовка
                        for (int j = i + 1; j < _categoryListBox.Items.Count; j++)
                        {
                            var nextItem = _categoryListBox.Items[j];
                            if (nextItem is string) break; // Следующий заголовок - останавливаемся
                            _categoryListBox.SetItemChecked(j, true);
                        }
                        break;
                    }
                }
            }
        }

        public List<BuiltInCategory> GetSelectedCategories()
        {
            return _categoryStates.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
        }

        private List<BuiltInCategory> GetRelevantCategories()
        {
            return new List<BuiltInCategory>
            {
                // АРХИТЕКТУРА
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Stairs,
                BuiltInCategory.OST_Rooms,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Ramps,
                BuiltInCategory.OST_Railings,
                BuiltInCategory.OST_Furniture,

                // ОВ
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_PipeCurves,

                // ВК
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory,
                BuiltInCategory.OST_PlumbingFixtures,
                BuiltInCategory.OST_MechanicalEquipment,

                // ЭЛ
                BuiltInCategory.OST_ElectricalEquipment,
                BuiltInCategory.OST_CommunicationDevices,
                BuiltInCategory.OST_NurseCallDevices,
                BuiltInCategory.OST_FireAlarmDevices,
                BuiltInCategory.OST_SecurityDevices,
                BuiltInCategory.OST_LightingFixtures,
                BuiltInCategory.OST_ElectricalFixtures,

                // КАБЕЛЬНЫЕ СИСТЕМЫ
                BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_CableTrayFitting,
                BuiltInCategory.OST_Conduit,
                BuiltInCategory.OST_ConduitFitting,

                // КОНСТРУКЦИИ
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralFoundation, // ИСПРАВЛЕНО!

                // ОБЩИЕ
                BuiltInCategory.OST_SpecialityEquipment,
                BuiltInCategory.OST_GenericModel
            };
        }

        // Вспомогательный класс для хранения категорий
        private class CategoryItem
        {
            public BuiltInCategory Category { get; }
            public string DisplayName { get; }

            public CategoryItem(BuiltInCategory category, string displayName)
            {
                Category = category;
                DisplayName = displayName;
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}