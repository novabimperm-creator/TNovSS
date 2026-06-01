using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using System.Windows.Forms;
using System.Drawing;

namespace TNovSS
{
    public partial class IntersectionResultsForm : System.Windows.Forms.Form
    {
        private UIApplication _uiApp;
        private List<IntersectionResult> _intersections;
        private string _stats;

        // ✅ ДОБАВЛЕН КОНСТРУКТОР С 3 АРГУМЕНТАМИ
        public IntersectionResultsForm(UIApplication uiApp, List<IntersectionResult> intersections, string stats)
        {
            _uiApp = uiApp;
            _intersections = intersections;
            _stats = stats;
            InitializeForm();
        }

        private void InitializeForm()
        {
            bool success = _intersections != null && _intersections.Count > 0;

            // Основные настройки формы
            this.Text = "Результаты проверки пересечений";
            this.Size = new System.Drawing.Size(1000, 600);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.BackColor = success ?
                System.Drawing.Color.FromArgb(240, 255, 240) :
                System.Drawing.Color.FromArgb(255, 240, 240);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(800, 400);

            // Заголовок
            var titleLabel = new System.Windows.Forms.Label();
            titleLabel.Text = success ? "НАЙДЕННЫЕ ПЕРЕСЕЧЕНИЯ" : "ПЕРЕСЕЧЕНИЙ НЕ НАЙДЕНО";
            titleLabel.Font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Bold);
            titleLabel.ForeColor = success ?
                System.Drawing.Color.FromArgb(0, 100, 0) :
                System.Drawing.Color.FromArgb(220, 53, 69);
            titleLabel.Location = new System.Drawing.Point(20, 20);
            titleLabel.Size = new System.Drawing.Size(950, 30);
            titleLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;

            // Статистика
            var statsLabel = new System.Windows.Forms.Label();
            statsLabel.Text = _stats ?? $"Найдено пересечений: {_intersections?.Count ?? 0}";
            statsLabel.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            statsLabel.ForeColor = System.Drawing.Color.FromArgb(100, 100, 100);
            statsLabel.Location = new System.Drawing.Point(20, 60);
            statsLabel.Size = new System.Drawing.Size(950, 25);
            statsLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            // Список пересечений
            if (success)
            {
                var listView = new System.Windows.Forms.ListView();
                listView.Location = new System.Drawing.Point(20, 100);
                listView.Size = new System.Drawing.Size(950, 400);
                listView.View = System.Windows.Forms.View.Details;
                listView.FullRowSelect = true;
                listView.GridLines = true;
                listView.Font = new System.Drawing.Font("Segoe UI", 9);
                listView.BackColor = System.Drawing.Color.White;

                // Колонки
                listView.Columns.Add("Текущий элемент", 250);
                listView.Columns.Add("ID текущего", 100);
                listView.Columns.Add("Связанный элемент", 250);
                listView.Columns.Add("ID связанного", 100);
                listView.Columns.Add("Связанный файл", 200);
                listView.Columns.Add("Действие", 100);

                // Заполняем список
                foreach (var intersection in _intersections)
                {
                    var item = new System.Windows.Forms.ListViewItem(new[] {
                        intersection.CurrentElementName ?? "Без имени",
                        intersection.CurrentElementId.IntegerValue.ToString(),
                        intersection.LinkedElementName ?? "Без имени",
                        intersection.LinkedElementId.IntegerValue.ToString(),
                        intersection.LinkDocumentName,
                        "Показать в 3D"
                    });
                    item.Tag = intersection;
                    listView.Items.Add(item);
                }

                // Обработчик двойного клика
                listView.MouseDoubleClick += (s, e) =>
                {
                    var info = listView.HitTest(e.X, e.Y);
                    if (info.Item != null)
                    {
                        ShowElementIn3DView(info.Item.Tag as IntersectionResult);
                    }
                };

                this.Controls.Add(listView);
            }

            // Кнопка OK
            var okButton = new System.Windows.Forms.Button();
            okButton.Text = "ЗАКРЫТЬ";
            okButton.Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold);
            okButton.Location = new System.Drawing.Point(450, 520);
            okButton.Size = new System.Drawing.Size(100, 35);
            okButton.BackColor = System.Drawing.Color.FromArgb(0, 123, 255);
            okButton.ForeColor = System.Drawing.Color.White;
            okButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            okButton.DialogResult = System.Windows.Forms.DialogResult.OK;

            this.Controls.AddRange(new System.Windows.Forms.Control[] {
                titleLabel, statsLabel, okButton
            });

            this.AcceptButton = okButton;
        }

        private void ShowElementIn3DView(IntersectionResult intersection)
        {
            if (intersection == null) return;

            try
            {
                UIDocument uiDoc = _uiApp.ActiveUIDocument;
                Document doc = uiDoc.Document;

                // Переключаемся на 3D вид
                View3D view3D = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate && v.Name.Contains("3D"));

                if (view3D == null)
                {
                    // Создаем новый 3D вид если нет существующего
                    using (Transaction t = new Transaction(doc, "Создание 3D вида"))
                    {
                        t.Start();
                        view3D = View3D.CreateIsometric(doc,
                            new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .First(x => x.ViewFamily == ViewFamily.ThreeDimensional).Id);
                        t.Commit();
                    }
                }

                // Активируем 3D вид
                uiDoc.ActiveView = view3D;

                // Показываем текущий элемент
                try
                {
                    Element currentElement = doc.GetElement(intersection.CurrentElementId);
                    if (currentElement != null)
                    {
                        uiDoc.Selection.SetElementIds(new List<ElementId> { intersection.CurrentElementId });
                        uiDoc.ShowElements(currentElement);
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Внимание", $"Не удалось показать текущий элемент: {ex.Message}");
                }

                TaskDialog.Show("Навигация",
                    $"Элемент показан в 3D виде.\n\n" +
                    $"Текущий элемент: {intersection.CurrentElementName} (ID: {intersection.CurrentElementId.IntegerValue})\n" +
                    $"Связанный элемент: {intersection.LinkedElementName} (ID: {intersection.LinkedElementId.IntegerValue})\n" +
                    $"Файл: {intersection.LinkDocumentName}");

            }
            catch (Exception ex)
            {
                TaskDialog.Show("Ошибка", $"Не удалось перейти к элементу: {ex.Message}");
            }
        }
    }
}