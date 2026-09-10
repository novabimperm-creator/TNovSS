using System;
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SchemeBuilder.Core;

namespace SchemeBuilder.Commands
{
    /// <summary>
    /// Кнопка «Анализ»: выгружает устройство модели в текстовый файл.
    ///
    /// Нужна для настройки плагина под конкретный проект: по отчёту видно, какие категории
    /// заполнены, как названы параметры, подключены ли связи с помещениями и чем набиты легенды.
    /// Модель при этом не меняется — команда только читает.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class AnalyzeModelCommand : IExternalCommand
    {
        private const string Title = "Анализ модели";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                TaskDialog.Show(Title, "Откройте проект.");
                return Result.Cancelled;
            }

            string path;
            try
            {
                path = ModelReport.Write(uiDocument.Document);
            }
            catch (Exception exception)
            {
                message = "Не удалось записать отчёт: " + exception.Message;
                return Result.Failed;
            }

            var dialog = new TaskDialog(Title)
            {
                MainInstruction = "Отчёт записан.",
                MainContent = path + "\n\nМодель не изменялась — команда только читала её.",
                CommonButtons = TaskDialogCommonButtons.Close,
                AllowCancellation = true
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть папку с отчётом");

            if (dialog.Show() == TaskDialogResult.CommandLink1) OpenFolder(path);

            return Result.Succeeded;
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch (Exception)
            {
                // Проводник недоступен — путь уже показан в диалоге, этого достаточно.
            }
        }
    }
}
