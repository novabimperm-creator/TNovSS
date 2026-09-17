using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SchemeBuilder.Core;
using SchemeBuilder.UI;
using TNovCommon;
using View = Autodesk.Revit.DB.View;

namespace SchemeBuilder.Commands
{
    /// <summary>
    /// Единственная кнопка раздела: пошаговый конструктор. Все решения принимаются в одном
    /// окне и в том порядке, в котором они нужны, а не россыпью отдельных команд.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenWizardCommand : IExternalCommand
    {
        private const string Title = "Структурная схема";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApplication = commandData.Application;
            UIDocument uiDocument = uiApplication.ActiveUIDocument;
            if (uiDocument == null)
            {
                RevitWindow.ShowDialog(new InfoWindow280("Откройте проект."), uiApplication);
                return Result.Cancelled;
            }

            Document document = uiDocument.Document;

            // Легенд может не быть вовсе: создать вид-легенду через API нельзя, это ограничение
            // Revit. Схеме легенда не нужна — мастер открывается и без неё.
            List<View> legends = LegendBuilder.FindLegends(document);

            SchemeSettings settings = SettingsStorage.Read(document);

            List<DeviceRow> rows;
            View legend;
            MatrixLayout matrix;

            var wizard = new WizardWindow(document, settings, legends);
            if (RevitWindow.ShowDialog(wizard, uiApplication) != true) return Result.Cancelled;

            rows = wizard.Rows;
            legend = wizard.SelectedLegend;
            matrix = wizard.Matrix;

            BuildResult legendResult = null;
            MatrixResult matrixResult = null;
            SheetResult sheetResult = null;
            View toShow = null;

            using (var transaction = new Transaction(document, "Структурная схема"))
            {
                transaction.Start();

                if (settings.BuildLegend && legend != null)
                {
                    legendResult = LegendBuilder.Build(document, legend, rows, settings);
                    toShow = legend;
                }

                if (settings.BuildMatrix)
                {
                    ViewDrafting view = MatrixBuilder.EnsureView(document, settings);

                    if (view == null)
                    {
                        matrixResult = new MatrixResult();
                        matrixResult.Notes.Add("В проекте нет типа чернового вида — схему строить не в чем.");
                    }
                    else
                    {
                        matrixResult = MatrixBuilder.Build(document, view, matrix, settings);
                        toShow = view;

                        if (settings.PlaceOnSheet)
                        {
                            sheetResult = SheetBuilder.Place(document, view, settings);
                        }
                    }
                }

                SettingsStorage.Write(document, settings);
                transaction.Commit();
            }

            // Открываем то, что построили последним: схему, если она была, иначе легенду.
            if (toShow != null) uiDocument.ActiveView = toShow;

            ShowReport(legendResult, matrixResult, sheetResult);

            return Result.Succeeded;
        }

        private static void ShowReport(BuildResult legend, MatrixResult matrix, SheetResult sheet)
        {
            var text = new StringBuilder();

            if (legend != null)
            {
                text.AppendLine("Легенда: строк " + legend.Created.ToString(CultureInfo.CurrentCulture) +
                                (legend.Lines > 0
                                    ? ", типов линий " + legend.Lines.ToString(CultureInfo.CurrentCulture)
                                    : string.Empty) + ".");

                if (legend.Skipped.Count > 0)
                {
                    // Причина у всех строк одна и та же, если образца нет: печатать её сотню раз незачем.
                    text.AppendLine("Без символа: " + legend.Skipped.Count.ToString(CultureInfo.CurrentCulture) +
                                    " — " + legend.Skipped[0]);
                }
            }

            if (matrix != null)
            {
                if (text.Length > 0) text.AppendLine();

                text.AppendLine("Схема: строк " + matrix.Rows.ToString(CultureInfo.CurrentCulture) +
                                ", ячеек " + matrix.Cells.ToString(CultureInfo.CurrentCulture) +
                                ", блоков " + matrix.Blocks.ToString(CultureInfo.CurrentCulture) + ".");

                if (matrix.Trunks > 0)
                {
                    text.AppendLine("Магистрали: шин " + matrix.Trunks.ToString(CultureInfo.CurrentCulture) +
                                    " — это регулярная гребёнка, разводку внутри ячеек доводят руками.");
                }

                if (matrix.WithoutUgo > 0)
                {
                    text.AppendLine("Без УГО: " + matrix.WithoutUgo.ToString(CultureInfo.CurrentCulture) +
                                    " — сопоставьте семейства кнопкой «УГО».");
                }

                foreach (string note in matrix.Notes) text.AppendLine(note);
            }

            if (sheet != null)
            {
                text.AppendLine();

                if (sheet.Sheet != null)
                {
                    text.AppendLine("Лист " + sheet.Sheet.SheetNumber + " «" + sheet.Sheet.Name + "»" +
                                    (sheet.Created ? " — заведён" : " — уже был") + ".");
                }

                if (sheet.Problem.Length > 0) text.AppendLine(sheet.Problem);
            }

            if (text.Length == 0) text.Append("Ничего не построено.");

            TaskDialog.Show(Title, text.ToString());
        }
    }
}
