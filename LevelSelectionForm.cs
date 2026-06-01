using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class LevelSelectionForm : BaseForm
    {
        public Level SelectedLevel { get; private set; }
        private List<Level> _levels;
        private Document _linkDoc;
        private System.Windows.Forms.ListView _levelListView;

        public LevelSelectionForm(Document linkDoc) : base()
        {
            _linkDoc = linkDoc;
            SelectedLevel = null;

            // Получаем все уровни из связанного файла
            _levels = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            InitializeForm();
            LoadLevels();

            // Настройка навигации
            this.ShowBackButton = true;
            this.NextButtonText = "Далее →";

            base.NextClicked += (s, e) => OnNextButtonClick();
            base.BackClicked += (s, e) => { this.DialogResult = DialogResult.Abort; this.Close(); };
            base.CancelClicked += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
        }

        private void InitializeForm()
        {
            base.Text = "Выбор этажа";
            base.Size = new System.Drawing.Size(800, 600);

            // Основной контейнер
            var mainContainer = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.Transparent,
                Padding = new Padding(0, 10, 0, 0)
            };

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label
            {
                Text = "ВЫБОР ЭТАЖА",
                Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold),
                ForeColor = base.TextColor,
                Dock = DockStyle.Top,
                Height = 35,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 10)
            };

            // Описание
            var descLabel = new System.Windows.Forms.Label
            {
                Text = "Выберите этаж, на котором будут размещаться элементы:",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = System.Drawing.Color.FromArgb(100, 100, 100),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 0, 10)
            };

            // ListView для уровней
            var listPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 0)
            };

            _levelListView = new System.Windows.Forms.ListView
            {
                Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                GridLines = true,
                Font = new System.Drawing.Font("Segoe UI", 9),
                BackColor = System.Drawing.Color.White,
                Scrollable = true
            };

            // Колонки
            _levelListView.Columns.Add("Этаж", 300);
            _levelListView.Columns.Add("Отметка", 150);
            _levelListView.Columns.Add("ID", 200);

            listPanel.Controls.Add(_levelListView);

            // Добавляем все элементы в mainContainer
            mainContainer.Controls.AddRange(new System.Windows.Forms.Control[] {
                listPanel,
                descLabel,
                titleLabel
            });

            // Добавляем mainContainer в ContentPanel
            base.ContentPanel.Controls.Add(mainContainer);

            // События
            _levelListView.SelectedIndexChanged += (s, e) => UpdatePreview();
            _levelListView.DoubleClick += (s, e) => OnNextButtonClick();
        }

        private void LoadLevels()
        {
            foreach (var level in _levels)
            {
                var item = new System.Windows.Forms.ListViewItem(new[] {
                    level.Name,
                    $"{level.Elevation:F3} м",
                    level.Id.ToString()
                });
                item.Tag = level;
                _levelListView.Items.Add(item);
            }

            // Выбираем первый элемент
            if (_levelListView.Items.Count > 0)
            {
                _levelListView.Items[0].Selected = true;
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (_levelListView.SelectedItems.Count > 0)
            {
                SelectedLevel = _levelListView.SelectedItems[0].Tag as Level;
                base.EnableNextButton(SelectedLevel != null);
            }
            else
            {
                SelectedLevel = null;
                base.EnableNextButton(false);
            }
        }

        private void OnNextButtonClick()
        {
            if (SelectedLevel != null)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите этаж", "Внимание",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}