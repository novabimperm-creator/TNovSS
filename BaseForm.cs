using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace TNovSS
{
    public partial class BaseForm : Form
    {
        // Стандартные цвета Windows
        protected readonly System.Drawing.Color FormBackground = System.Drawing.Color.White;
        protected readonly System.Drawing.Color ControlBackground = System.Drawing.Color.White;
        protected readonly System.Drawing.Color TextColor = System.Drawing.Color.FromArgb(64, 64, 64);
        protected readonly System.Drawing.Color AccentColor = System.Drawing.Color.FromArgb(0, 102, 204);
        protected readonly System.Drawing.Color BorderColor = System.Drawing.Color.FromArgb(200, 200, 200);
        protected readonly System.Drawing.Color ButtonColor = System.Drawing.Color.FromArgb(0, 102, 204);
        protected readonly System.Drawing.Color ButtonHoverColor = System.Drawing.Color.FromArgb(0, 86, 179);
        protected readonly System.Drawing.Color CancelButtonColor = System.Drawing.Color.FromArgb(108, 117, 125);
        protected readonly System.Drawing.Color CancelButtonHoverColor = System.Drawing.Color.FromArgb(86, 94, 100);
        protected readonly System.Drawing.Color BackButtonColor = System.Drawing.Color.FromArgb(108, 117, 125);
        protected readonly System.Drawing.Color BackButtonHoverColor = System.Drawing.Color.FromArgb(86, 94, 100);

        // Элементы навигации - ЯВНО УКАЗЫВАЕМ System.Windows.Forms
        protected System.Windows.Forms.Panel NavigationPanel;
        protected System.Windows.Forms.Button BackButton;
        protected System.Windows.Forms.Button NextButton;
        protected new System.Windows.Forms.Button CancelButton;
        protected System.Windows.Forms.Panel ContentPanel;

        // События
        public event EventHandler BackClicked;
        public event EventHandler NextClicked;
        public event EventHandler CancelClicked;

        // Свойства (runtime-only, не для WinForms Designer)
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowBackButton
        {
            get => BackButton != null && BackButton.Visible;
            set
            {
                if (BackButton != null)
                    BackButton.Visible = value;
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string NextButtonText
        {
            get => NextButton != null ? NextButton.Text : string.Empty;
            set
            {
                if (NextButton != null)
                    NextButton.Text = value;
            }
        }

        // КОНСТРУКТОР
        public BaseForm()
        {
            InitializeBaseForm();
        }

        private void InitializeBaseForm()
        {
            // Основные настройки формы
            this.Text = "Pikachu Plugin";
            this.Size = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = FormBackground;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Padding = new Padding(20, 15, 20, 15);
            this.Font = new System.Drawing.Font("Segoe UI", 9);

            // Панель для содержимого - ОСНОВНАЯ ОБЛАСТЬ СОДЕРЖИМОГО
            ContentPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.Transparent,
                Padding = new Padding(0, 0, 0, 50) // Оставляем место для навигации
            };

            // Панель навигации - ВНИЗУ ФОРМЫ
            NavigationPanel = new System.Windows.Forms.Panel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                BackColor = System.Drawing.Color.Transparent,
                Padding = new Padding(0, 5, 0, 0)
            };

            // Кнопка "Назад" - ЛЕВЫЙ НИЖНИЙ УГОЛ
            BackButton = CreateStandardButton("← Назад", BackButtonColor, BackButtonHoverColor);
            BackButton.Click += (s, e) => BackClicked?.Invoke(this, EventArgs.Empty);
            BackButton.Visible = false;

            // Кнопка "Далее" - ПРАВЫЙ НИЖНИЙ УГОЛ
            NextButton = CreateStandardButton("Далее →", ButtonColor, ButtonHoverColor);
            NextButton.Click += (s, e) => NextClicked?.Invoke(this, EventArgs.Empty);

            // Кнопка "Отмена" - ПРАВЫЙ НИЖНИЙ УГОЛ (рядом с Далее)
            CancelButton = CreateStandardButton("Отмена", CancelButtonColor, CancelButtonHoverColor);
            CancelButton.Click += (s, e) => CancelClicked?.Invoke(this, EventArgs.Empty);

            // Расположение кнопок
            NavigationPanel.Controls.AddRange(new Control[] { BackButton, NextButton, CancelButton });

            // Добавляем панели на форму
            this.Controls.Add(ContentPanel);
            this.Controls.Add(NavigationPanel);

            // Подписываемся на изменение размера формы
            this.SizeChanged += (s, e) => UpdateButtonPositions();

            // Обновляем позиции кнопок при загрузке формы
            this.Load += (s, e) => UpdateButtonPositions();
        }

        // Методы
        protected System.Windows.Forms.Button CreateStandardButton(string text, System.Drawing.Color normalColor, System.Drawing.Color hoverColor)
        {
            var button = new System.Windows.Forms.Button
            {
                Text = text,
                BackColor = normalColor,
                ForeColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new System.Drawing.Font("Segoe UI", 9),
                Cursor = Cursors.Hand,
                Size = new System.Drawing.Size(100, 32),
                TabStop = false,
                FlatAppearance = { BorderSize = 0 }
            };

            // Простые hover эффекты
            button.MouseEnter += (s, e) => button.BackColor = hoverColor;
            button.MouseLeave += (s, e) => button.BackColor = normalColor;

            return button;
        }

        protected System.Windows.Forms.Panel CreateContentPanel()
        {
            var panel = new System.Windows.Forms.Panel
            {
                BackColor = System.Drawing.Color.White,
                Padding = new Padding(15)
            };

            // Рисуем границу вручную
            panel.Paint += (sender, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, panel.ClientRectangle,
                    BorderColor, ButtonBorderStyle.Solid);
            };

            return panel;
        }

        protected void UpdateButtonPositions()
        {
            if (NavigationPanel == null || BackButton == null || NextButton == null || CancelButton == null)
                return;

            int margin = 10;
            int buttonWidth = 100;
            int buttonHeight = 32;

            // Отмена - правый нижний угол
            CancelButton.Location = new System.Drawing.Point(
                NavigationPanel.Width - buttonWidth - margin,
                (NavigationPanel.Height - buttonHeight) / 2);

            // Далее - слева от Отмены
            NextButton.Location = new System.Drawing.Point(
                CancelButton.Left - buttonWidth - margin,
                (NavigationPanel.Height - buttonHeight) / 2);

            // Назад - левый нижний угол
            BackButton.Location = new System.Drawing.Point(
                margin,
                (NavigationPanel.Height - buttonHeight) / 2);
        }

        public void EnableNextButton(bool enabled)
        {
            if (NextButton != null)
            {
                NextButton.Enabled = enabled;
                NextButton.BackColor = enabled ? ButtonColor : System.Drawing.Color.FromArgb(200, 200, 200);
            }
        }

        // Заглушки для совместимости с существующим кодом
        protected System.Windows.Forms.Panel CreateCardPanel() => CreateContentPanel();
    }
}