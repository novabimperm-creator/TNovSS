using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SchemeBuilder.Core;
using SchemeBuilder.UI;

namespace SchemeBuilder.Commands
{
    /// <summary>
    /// Сопоставление «прибор → его УГО на схеме». Делается один раз на модель, выбор хранится
    /// в ней же.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class MapUgoCommand : IExternalCommand
    {
        private const string Title = "УГО оборудования";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show(Title, "Откройте проект.");
                return Result.Cancelled;
            }

            Document document = uiDocument.Document;
            SchemeSettings settings = SettingsStorage.Read(document);

            // Обходим все известные категории, а не отмеченные в конструкторе: сопоставление
            // делается один раз и обычно раньше, чем выбраны категории, — а на свежей модели
            // отмечена всего одна, и половина приборов осталась бы без УГО.
            List<DeviceRow> rows = DeviceScanner.Scan(document, DeviceScanner.KnownCategories, settings);
            if (rows.Count == 0)
            {
                TaskDialog.Show(Title, "Оборудования в модели не нашлось — сопоставлять нечего.");
                return Result.Cancelled;
            }

            List<UgoMatch> matches;

            using (var form = new UgoForm(document, settings, rows))
            {
                if (form.ShowDialog() != DialogResult.OK) return Result.Cancelled;

                matches = form.Matches;
            }

            using (var transaction = new Transaction(document, "УГО оборудования"))
            {
                transaction.Start();

                foreach (UgoMatch match in matches)
                {
                    settings.SetUgo(match.Row.TypeId, match.Selected?.Key);
                }

                SettingsStorage.Write(document, settings);
                transaction.Commit();
            }

            ShowReport(matches);
            return Result.Succeeded;
        }

        private static void ShowReport(List<UgoMatch> matches)
        {
            int chosen = matches.Count(m => m.Selected != null);
            int without = matches.Count - chosen;

            var text = new StringBuilder();
            text.AppendLine("Типоразмеров: " + matches.Count.ToString(CultureInfo.CurrentCulture) +
                            ", УГО назначено: " + chosen.ToString(CultureInfo.CurrentCulture) + ".");

            if (without > 0)
            {
                text.AppendLine("Без УГО осталось: " + without.ToString(CultureInfo.CurrentCulture) +
                                " — в схеме такие блоки будут текстовыми.");
            }

            // Сопоставление ничего не перерисовывает, и без этой строки кажется, что кнопка
            // не сработала: схема-то на экране прежняя.
            text.AppendLine();
            text.AppendLine("Сопоставление сохранено в модели. На уже построенной схеме УГО сами " +
                            "не появятся — постройте её заново: «Конструктор» → «Построить».");

            TaskDialog.Show(Title, text.ToString());
        }
    }
}
