using System;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class ProgressForm : Form
    {
        // Убрали неиспользуемые поля или используем их
        private ProgressBar _progressBar;
        private Label _statusLabel;
        private Button _cancelButton;
        private bool _cancelled = false;
        private Action _cancelAction;

        public ProgressForm(string title, string initialMessage)
        {
            InitializeForm(title, initialMessage);
        }

        private void InitializeForm(string title, string initialMessage)
        {
            this.Text = title;
            this.Size = new Size(400, 150);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.ControlBox = false;
            this.BackColor = Color.FromArgb(240, 245, 255);

            // Заголовок
            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(0, 80, 160);
            titleLabel.Location = new Point(20, 15);
            titleLabel.Size = new Size(360, 25);
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;

            // Статусное сообщение
            _statusLabel = new Label();
            _statusLabel.Text = initialMessage;
            _statusLabel.Font = new Font("Segoe UI", 9);
            _statusLabel.ForeColor = Color.FromArgb(100, 100, 100);
            _statusLabel.Location = new Point(20, 45);
            _statusLabel.Size = new Size(360, 20);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            // Прогресс-бар
            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(20, 75);
            _progressBar.Size = new Size(360, 20);
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Value = 0;

            // Кнопка отмены
            _cancelButton = new Button();
            _cancelButton.Text = "ОТМЕНА";
            _cancelButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            _cancelButton.Location = new Point(150, 105);
            _cancelButton.Size = new Size(100, 25);
            _cancelButton.BackColor = Color.FromArgb(108, 117, 125);
            _cancelButton.ForeColor = Color.White;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.Click += (s, e) =>
            {
                _cancelled = true;
                _cancelAction?.Invoke();
                this.Close();
            };

            this.Controls.AddRange(new Control[] {
                titleLabel, _statusLabel, _progressBar, _cancelButton
            });
        }

        public void UpdateProgress(int progress, string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateProgress(progress, message)));
                return;
            }

            _progressBar.Value = Math.Min(progress, 100);
            _statusLabel.Text = message;
            System.Windows.Forms.Application.DoEvents();
        }

        public void UpdateMessage(string message)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateMessage(message)));
                return;
            }

            _statusLabel.Text = message;
            System.Windows.Forms.Application.DoEvents();
        }

        public void SetCancelHandler(Action cancelAction)
        {
            _cancelAction = cancelAction;
        }

        public bool IsCancelled()
        {
            return _cancelled;
        }
    }
}