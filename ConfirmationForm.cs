using System;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class ConfirmationForm : BaseForm
    {
        public bool UserConfirmed { get; private set; }

        public ConfirmationForm(string familyAName, string familyBName,
                               int instanceCount, string distance, string levelName = "") : base()
        {
            UserConfirmed = false;
            InitializeForm(familyAName, familyBName, instanceCount, distance, levelName);

            // Настройка навигации
            this.ShowBackButton = true;
            this.NextButtonText = "Завершить размещение";

            base.NextClicked += (s, e) => OnConfirmButtonClick();
            base.BackClicked += (s, e) => this.Close();
            base.CancelClicked += (s, e) => OnCancelButtonClick();
        }

        private void InitializeForm(string familyAName, string familyBName,
                                   int instanceCount, string distance, string levelName)
        {
            base.Text = "Подтверждение операции";
            base.Size = new Size(800, 600);

            // Заголовок
            var titleLabel = new Label
            {
                Text = "ПОДТВЕРЖДЕНИЕ РАЗМЕЩЕНИЯ",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = base.TextColor,
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Панель с информацией
            var infoPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(30)
            };

            // Сводка
            var summaryLabel = new Label
            {
                Text = "Сводка настроек:",
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = base.AccentColor,
                Location = new Point(0, 20),
                Size = new Size(700, 30),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Семейство А
            var familyAPanel = CreateInfoRow("Семейство А (ОВ/ВК):", familyAName, 70);

            // Семейство Б
            var familyBPanel = CreateInfoRow("Семейство Б (Электро):", familyBName, 110);

            // Количество
            var countPanel = CreateInfoRow("Количество элементов:", instanceCount.ToString(), 150);
            var countValue = countPanel.Controls[1] as Label;
            if (countValue != null)
            {
                countValue.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            }

            // Расстояние
            var distancePanel = CreateInfoRow("Расстояние размещения:", distance, 190);

            // Уровень
            var levelPanel = CreateInfoRow("Этаж:",
                string.IsNullOrEmpty(levelName) ? "Все этажи" : levelName,
                230);

            // Разделитель
            var separator = new System.Windows.Forms.Panel
            {
                BackColor = base.BorderColor,
                Location = new Point(20, 280),
                Size = new Size(700, 1)
            };

            // Предупреждение
            var warningPanel = new System.Windows.Forms.Panel
            {
                Location = new Point(20, 300),
                Size = new Size(700, 80),
                BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(255, 243, 205)
            };

            var warningIcon = new Label
            {
                Text = "⚠",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(15, 30),
                Size = new Size(30, 30),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var warningText = new Label
            {
                Text = $"ВНИМАНИЕ: Будет создано {instanceCount} новых элементов\n" +
                      $"Это действие нельзя отменить автоматически",
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(133, 100, 4),
                Location = new Point(60, 10),
                Size = new Size(620, 60),
                TextAlign = ContentAlignment.MiddleLeft
            };

            warningPanel.Controls.AddRange(new Control[] { warningIcon, warningText });

            // Добавляем все элементы в infoPanel
            infoPanel.Controls.AddRange(new Control[] {
                summaryLabel, familyAPanel, familyBPanel, countPanel,
                distancePanel, levelPanel, separator, warningPanel
            });

            // Добавляем все элементы в ContentPanel
            base.ContentPanel.Controls.AddRange(new Control[] {
                titleLabel,
                infoPanel
            });
        }

        private System.Windows.Forms.Panel CreateInfoRow(string labelText, string valueText, int top)
        {
            var panel = new System.Windows.Forms.Panel
            {
                Location = new Point(20, top),
                Size = new Size(700, 30),
                BackColor = Color.Transparent
            };

            var label = new Label
            {
                Text = labelText,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                ForeColor = base.TextColor,
                Location = new Point(0, 5),
                Size = new Size(300, 25),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var value = new Label
            {
                Text = valueText,
                Font = new Font("Segoe UI", 10),
                ForeColor = base.TextColor,
                Location = new Point(310, 5),
                Size = new Size(380, 25),
                TextAlign = ContentAlignment.MiddleRight
            };

            panel.Controls.AddRange(new Control[] { label, value });
            return panel;
        }

        private void OnConfirmButtonClick()
        {
            UserConfirmed = true;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnCancelButtonClick()
        {
            UserConfirmed = false;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}