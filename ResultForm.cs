using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public class ResultForm : BaseForm
    {
        public ResultForm(List<ElementId> createdElements) : base()
        {
            InitializeForm(createdElements);

            // Настройка навигации
            this.ShowBackButton = false;
            this.NextButtonText = "Готово";

            base.NextClicked += (s, e) => this.Close();
            base.CancelClicked += (s, e) => this.Close();
        }

        private void InitializeForm(List<ElementId> createdElements)
        {
            bool success = createdElements != null && createdElements.Count > 0;

            base.Text = "Результат размещения";
            base.Size = new System.Drawing.Size(600, 400);

            // Иконка результата
            var iconLabel = new System.Windows.Forms.Label
            {
                Text = success ? "✓" : "✗",
                Font = new System.Drawing.Font("Segoe UI", 48),
                ForeColor = success ? System.Drawing.Color.Green : System.Drawing.Color.Red,
                Dock = DockStyle.Top,
                Height = 100,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                BackColor = System.Drawing.Color.Transparent
            };

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label
            {
                Text = success ? "РАЗМЕЩЕНИЕ УСПЕШНО!" : "РАЗМЕЩЕНИЕ НЕ ВЫПОЛНЕНО",
                Font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold),
                ForeColor = success ? System.Drawing.Color.Green : System.Drawing.Color.Red,
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                BackColor = System.Drawing.Color.Transparent
            };

            // Сообщение
            var messageLabel = new System.Windows.Forms.Label
            {
                Text = success ?
                    $"Успешно размещено {createdElements.Count} элементов" :
                    "Элементы не были размещены",
                Font = new System.Drawing.Font("Segoe UI", 11),
                ForeColor = System.Drawing.Color.FromArgb(100, 100, 100),
                Dock = DockStyle.Top,
                Height = 40,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                BackColor = System.Drawing.Color.Transparent
            };

            // Добавляем все элементы в ContentPanel
            base.ContentPanel.Controls.AddRange(new System.Windows.Forms.Control[] {
                iconLabel,
                titleLabel,
                messageLabel
            });
        }
    }
}