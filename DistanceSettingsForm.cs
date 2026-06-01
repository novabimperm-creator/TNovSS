using Autodesk.Revit.DB;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TNovSS
{
    public partial class DistanceSettingsForm : BaseForm
    {
        public double Distance { get; private set; }
        public XYZ Direction { get; private set; }

        private System.Windows.Forms.RadioButton _defaultDistanceRadio;
        private System.Windows.Forms.RadioButton _customDistanceRadio;
        private System.Windows.Forms.NumericUpDown _distanceNumeric;
        private System.Windows.Forms.ComboBox _unitsComboBox;
        private System.Windows.Forms.ComboBox _directionComboBox;
        private System.Windows.Forms.Label _previewText;

        public DistanceSettingsForm() : base()
        {
            Distance = 0.5;
            Direction = XYZ.BasisZ;

            InitializeForm();

            this.ShowBackButton = true;
            this.NextButtonText = "Далее →";

            base.NextClicked += (s, e) => OnNextButtonClick();
            base.BackClicked += (s, e) => this.Close();
            base.CancelClicked += (s, e) => this.Close();
        }

        private void InitializeForm()
        {
            base.Text = "Настройка расстояния";
            base.Size = new System.Drawing.Size(700, 550);

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label
            {
                Text = "НАСТРОЙКА РАССТОЯНИЯ РАЗМЕЩЕНИЯ",
                Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold),
                ForeColor = base.TextColor,
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            // Панель настроек
            var settingsPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20)
            };

            _defaultDistanceRadio = new System.Windows.Forms.RadioButton
            {
                Text = "Разместить рядом (смещение 0.5 м по вертикали вверх)",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = base.TextColor,
                Location = new System.Drawing.Point(20, 20),
                Size = new System.Drawing.Size(600, 25),
                Checked = true
            };

            _customDistanceRadio = new System.Windows.Forms.RadioButton
            {
                Text = "Задать свое расстояние:",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = base.TextColor,
                Location = new System.Drawing.Point(20, 60),
                Size = new System.Drawing.Size(250, 25)
            };

            _distanceNumeric = new System.Windows.Forms.NumericUpDown
            {
                Location = new System.Drawing.Point(20, 95),
                Size = new System.Drawing.Size(120, 24),
                Font = new System.Drawing.Font("Segoe UI", 9),
                DecimalPlaces = 2,
                Minimum = -100,
                Maximum = 100,
                Value = (decimal)Distance,
                Increment = 0.1M,
                Enabled = false
            };

            _unitsComboBox = new System.Windows.Forms.ComboBox
            {
                Location = new System.Drawing.Point(150, 95),
                Size = new System.Drawing.Size(120, 24),
                Font = new System.Drawing.Font("Segoe UI", 9),
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Items = { "метры", "миллиметры" },
                SelectedIndex = 0,
                Enabled = false
            };

            // Надпись для направления
            var directionLabel = new System.Windows.Forms.Label
            {
                Text = "Направление:",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = base.TextColor,
                Location = new System.Drawing.Point(20, 135),
                Size = new System.Drawing.Size(100, 25),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            _directionComboBox = new System.Windows.Forms.ComboBox
            {
                Location = new System.Drawing.Point(120, 135),
                Size = new System.Drawing.Size(250, 24),
                Font = new System.Drawing.Font("Segoe UI", 9),
                DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList,
                Enabled = false
            };

            _directionComboBox.Items.AddRange(new object[] {
                "Вверх (по оси Z)",
                "Вниз (по оси -Z)",
                "Вправо (по оси X)",
                "Влево (по оси -X)",
                "Вперед (по оси Y)",
                "Назад (по оси -Y)"
            });
            _directionComboBox.SelectedIndex = 0;

            // Панель предпросмотра
            var previewPanel = new System.Windows.Forms.Panel
            {
                Location = new System.Drawing.Point(20, 180),
                Size = new System.Drawing.Size(620, 80),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.FromArgb(248, 249, 250)
            };

            var previewTitle = new System.Windows.Forms.Label
            {
                Text = "Предпросмотр:",
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
                ForeColor = base.AccentColor,
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(600, 25),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            _previewText = new System.Windows.Forms.Label
            {
                Name = "previewText",
                Text = "Размещение на расстоянии 0.5 м выше элемента",
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = base.TextColor,
                Location = new System.Drawing.Point(10, 40),
                Size = new System.Drawing.Size(600, 30),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            previewPanel.Controls.AddRange(new System.Windows.Forms.Control[] { previewTitle, _previewText });

            // Обработчики событий для переключения режимов
            _defaultDistanceRadio.CheckedChanged += (s, e) => UpdateControls();
            _customDistanceRadio.CheckedChanged += (s, e) => UpdateControls();
            _distanceNumeric.ValueChanged += (s, e) => UpdatePreview();
            _unitsComboBox.SelectedIndexChanged += (s, e) => UpdatePreview();
            _directionComboBox.SelectedIndexChanged += (s, e) => UpdatePreview();

            settingsPanel.Controls.AddRange(new System.Windows.Forms.Control[] {
                _defaultDistanceRadio, _customDistanceRadio, _distanceNumeric,
                _unitsComboBox, directionLabel, _directionComboBox, previewPanel
            });

            // Добавляем все элементы в ContentPanel
            base.ContentPanel.Controls.AddRange(new System.Windows.Forms.Control[] {
                titleLabel,
                settingsPanel
            });

            UpdateControls();
        }

        private void UpdateControls()
        {
            bool customMode = _customDistanceRadio.Checked;

            _distanceNumeric.Enabled = customMode;
            _unitsComboBox.Enabled = customMode;
            _directionComboBox.Enabled = customMode;

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            double distance = 0.5;
            string units = "м";
            string direction = "вверх";

            if (_customDistanceRadio.Checked)
            {
                distance = (double)_distanceNumeric.Value;
                units = _unitsComboBox.SelectedItem?.ToString() == "миллиметры" ? "мм" : "м";

                switch (_directionComboBox.SelectedIndex)
                {
                    case 0: direction = "вверх"; break;
                    case 1: direction = "вниз"; break;
                    case 2: direction = "вправо"; break;
                    case 3: direction = "влево"; break;
                    case 4: direction = "вперед"; break;
                    case 5: direction = "назад"; break;
                }
            }

            _previewText.Text = $"Размещение на расстоянии {distance} {units} {direction} от элемента";
        }

        private void OnNextButtonClick()
        {
            if (_customDistanceRadio.Checked)
            {
                if (_unitsComboBox.SelectedItem?.ToString() == "миллиметры")
                {
                    Distance = (double)_distanceNumeric.Value / 1000.0;
                }
                else
                {
                    Distance = (double)_distanceNumeric.Value;
                }
            }
            else
            {
                Distance = 0.5;
            }

            switch (_directionComboBox.SelectedIndex)
            {
                case 0: Direction = XYZ.BasisZ; break;
                case 1: Direction = -XYZ.BasisZ; break;
                case 2: Direction = XYZ.BasisX; break;
                case 3: Direction = -XYZ.BasisX; break;
                case 4: Direction = XYZ.BasisY; break;
                case 5: Direction = -XYZ.BasisY; break;
                default: Direction = XYZ.BasisZ; break;
            }

            this.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.Close();
        }
    }
}