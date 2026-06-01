using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class LinkSelectionForm : BaseForm
    {
        public RevitLinkInstance SelectedLink { get; private set; }

        private Document _doc;
        private List<RevitLinkInstance> _allLinks;
        private System.Windows.Forms.ListView _linkListView;
        private System.Windows.Forms.TextBox _searchTextBox;
        private System.Windows.Forms.Label _previewLabel;

        public LinkSelectionForm(Document doc) : base()
        {
            _doc = doc;
            SelectedLink = null;

            // Получаем все связанные файлы
            _allLinks = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .Where(link => link.GetLinkDocument() != null)
                .OrderBy(link => link.Name)
                .ToList();

            InitializeForm();
            LoadLinks();

            // Настройка навигации - первая форма, кнопка "Назад" скрыта
            this.ShowBackButton = false;
            this.NextButtonText = "Далее →";

            base.NextClicked += (s, e) => OnNextButtonClick();
            base.CancelClicked += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };

            // Кнопка Назад не нужна на первой форме
            base.BackClicked += (s, e) => { this.DialogResult = DialogResult.Abort; this.Close(); };
        }

        private void InitializeForm()
        {
            base.Text = "Выбор связанного файла";
            base.Size = new System.Drawing.Size(900, 700);

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
                Text = "ВЫБОР СВЯЗАННОГО ФАЙЛА",
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
                Text = "Выберите связанный файл Revit для размещения элементов рядом с его объектами:",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = System.Drawing.Color.FromArgb(100, 100, 100),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 0, 0, 10)
            };

            // Панель поиска
            var searchPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Top,
                Height = 45,
                Padding = new Padding(0, 0, 0, 10)
            };

            var searchLabel = new System.Windows.Forms.Label
            {
                Text = "Поиск:",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = base.TextColor,
                Location = new System.Drawing.Point(0, 12),
                Size = new System.Drawing.Size(50, 20),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            _searchTextBox = new System.Windows.Forms.TextBox
            {
                Location = new System.Drawing.Point(60, 10),
                Size = new System.Drawing.Size(250, 24),
                Font = new System.Drawing.Font("Segoe UI", 9),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle
            };
            _searchTextBox.TextChanged += (s, e) => FilterLinks();

            var clearButton = new System.Windows.Forms.Button
            {
                Text = "Очистить",
                Font = new System.Drawing.Font("Segoe UI", 9),
                Location = new System.Drawing.Point(320, 10),
                Size = new System.Drawing.Size(80, 24),
                BackColor = System.Drawing.Color.FromArgb(108, 117, 125),
                ForeColor = System.Drawing.Color.White,
                FlatStyle = System.Windows.Forms.FlatStyle.Flat
            };
            clearButton.Click += (s, e) =>
            {
                _searchTextBox.Text = "";
                FilterLinks();
            };

            searchPanel.Controls.AddRange(new System.Windows.Forms.Control[] { searchLabel, _searchTextBox, clearButton });

            // ListView для связанных файлов
            var listPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 10)
            };

            _linkListView = new System.Windows.Forms.ListView
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
            _linkListView.Columns.Add("Имя файла", 400);
            _linkListView.Columns.Add("Тип", 200);
            _linkListView.Columns.Add("Статус", 150);

            listPanel.Controls.Add(_linkListView);

            // Панель предпросмотра
            var previewPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.FromArgb(248, 249, 250),
                Margin = new Padding(0, 10, 0, 0)
            };

            _previewLabel = new System.Windows.Forms.Label
            {
                Name = "previewLabel",
                Dock = System.Windows.Forms.DockStyle.Fill,
                Text = "Выбрано: ничего",
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
                ForeColor = base.AccentColor,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };

            previewPanel.Controls.Add(_previewLabel);

            // Добавляем все элементы в mainContainer
            // ВАЖНО: добавляем в обратном порядке из-за DockStyle
            mainContainer.Controls.AddRange(new System.Windows.Forms.Control[] {
                listPanel,
                previewPanel,
                searchPanel,
                descLabel,
                titleLabel
            });

            // Добавляем mainContainer в ContentPanel
            base.ContentPanel.Controls.Add(mainContainer);

            // События
            _linkListView.SelectedIndexChanged += (s, e) => UpdatePreview();
            _linkListView.DoubleClick += (s, e) => OnNextButtonClick();
            _searchTextBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) FilterLinks();
            };
        }

        private void LoadLinks()
        {
            foreach (var link in _allLinks)
            {
                var linkDoc = link.GetLinkDocument();
                if (linkDoc == null) continue;

                var item = new System.Windows.Forms.ListViewItem(new[] {
                    link.Name,
                    "Revit Link",
                    linkDoc.IsLinked ? "Связан" : "Ошибка"
                });
                item.Tag = link;
                _linkListView.Items.Add(item);
            }

            // Выбираем первый элемент
            if (_linkListView.Items.Count > 0)
            {
                _linkListView.Items[0].Selected = true;
                UpdatePreview();
            }
        }

        private void FilterLinks()
        {
            string searchText = _searchTextBox.Text.Trim().ToLower();

            _linkListView.Items.Clear();
            _linkListView.BeginUpdate();

            var filtered = _allLinks.Where(link =>
                link.Name.ToLower().Contains(searchText) ||
                (link.GetLinkDocument()?.Title?.ToLower() ?? "").Contains(searchText));

            foreach (var link in filtered)
            {
                var linkDoc = link.GetLinkDocument();
                var item = new System.Windows.Forms.ListViewItem(new[] {
                    link.Name,
                    "Revit Link",
                    linkDoc != null ? "Связан" : "Ошибка"
                });
                item.Tag = link;
                _linkListView.Items.Add(item);
            }

            _linkListView.EndUpdate();
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_linkListView.SelectedItems.Count > 0)
            {
                var selectedItem = _linkListView.SelectedItems[0];
                var fileName = selectedItem.SubItems[0].Text;

                _previewLabel.Text = $"Выбрано: {fileName}";
                SelectedLink = selectedItem.Tag as RevitLinkInstance;
                base.EnableNextButton(SelectedLink != null);
            }
            else
            {
                _previewLabel.Text = "Выбрано: ничего";
                SelectedLink = null;
                base.EnableNextButton(false);
            }
        }

        private void OnNextButtonClick()
        {
            if (SelectedLink != null)
            {
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show("Пожалуйста, выберите связанный файл", "Внимание",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
