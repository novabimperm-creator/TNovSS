using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;

namespace SchemeBuilder
{
    /// <summary>
    /// Точка входа плагина: строит вкладку «Структурная схема» на ленте Revit.
    /// Кнопка одна — весь раздел проходится пошаговым конструктором.
    /// </summary>
    public class App : IExternalApplication
    {
        private const string TabName = "Структурная схема";
        private const string PanelName = "Раздел СС";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Вкладка уже создана другим плагином или прошлой загрузкой — это нормально.
            }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var wizard = new PushButtonData(
                "SchemeBuilder_Wizard",
                "Конструктор",
                assemblyPath,
                "SchemeBuilder.Commands.OpenWizardCommand")
            {
                ToolTip = "Пошаговый сбор раздела СС: оборудование, коды, зоны, легенда, схема.",
                LongDescription =
                    "Шесть шагов в том порядке, в котором решения всё равно приходится принимать:\n\n" +
                    "1. Оборудование — какие категории считать своими.\n" +
                    "2. Коды и наименования — код из префикса марки, наименование из ADSK_Наименование, " +
                    "и то и другое правится.\n" +
                    "3. Зоны — чем считать ячейку схемы; помещения ищутся и в связях.\n" +
                    "4. Оформление — легенда, размеры колонок, проверка компонента-образца.\n" +
                    "5. Схема — матрица целиком, как она встанет на лист.\n" +
                    "6. Построение — что именно будет нарисовано.\n\n" +
                    "Каждый шаг показывает результат предыдущего, поэтому ошибка видна сразу, а не на " +
                    "готовом листе. Настройки и правки хранятся в модели."
            };
            SetIcons(wizard, "legend");
            panel.AddItem(wizard);

            var ugo = new PushButtonData(
                "SchemeBuilder_Ugo",
                "УГО",
                assemblyPath,
                "SchemeBuilder.Commands.MapUgoCommand")
            {
                ToolTip = "Находит УГО внутри семейств приборов и запоминает, какое из них ставить в схему.",
                LongDescription =
                    "Плоское УГО прибора лежит вложенным семейством внутри его собственного: типовой " +
                    "аннотацией либо элементом узла. Плагин открывает каждое семейство, показывает найденное " +
                    "и даёт выбрать нужное, если УГО несколько.\n\n" +
                    "Выбранное загружается в проект и ставится в блоки схемы; то, что в проекте уже есть, " +
                    "не перезаписывается. Обход долгий — семейства открываются по одному, зато делается он " +
                    "один раз: выбор хранится в модели."
            };
            SetIcons(ugo, "legend");
            panel.AddItem(ugo);

            var analyze = new PushButtonData(
                "SchemeBuilder_Analyze",
                "Анализ",
                assemblyPath,
                "SchemeBuilder.Commands.AnalyzeModelCommand")
            {
                ToolTip = "Выгружает устройство модели в текстовый файл — для настройки плагина под проект.",
                LongDescription =
                    "В отчёт попадает: заполненность категорий, типы оборудования с примерами марок, полный " +
                    "список параметров образца каждой категории, наличие помещений в модели и в связях, " +
                    "содержимое легенд, типы текста и уровни.\n\n" +
                    "Модель не изменяется — команда только читает. Файл кладётся в папку профиля, путь " +
                    "показывается после выгрузки."
            };
            SetIcons(analyze, "scope");
            panel.AddItem(analyze);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        /// <summary>
        /// Подставляет иконки из встроенных ресурсов. Отсутствие иконки не должно ронять загрузку плагина.
        /// </summary>
        private static void SetIcons(PushButtonData button, string baseName)
        {
            button.Image = LoadEmbeddedImage(baseName + "16.png");
            button.LargeImage = LoadEmbeddedImage(baseName + "32.png");
        }

        private static BitmapImage LoadEmbeddedImage(string fileName)
        {
            try
            {
                string resourceName = "SchemeBuilder.Resources." + fileName;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;

                    var buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);

                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = new MemoryStream(buffer);
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
