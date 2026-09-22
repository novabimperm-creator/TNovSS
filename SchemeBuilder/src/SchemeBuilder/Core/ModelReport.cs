using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

using static TNovCommon.ElementIdCompat;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Выгрузка устройства модели в текстовый отчёт.
    ///
    /// Нужен затем, что настраивать плагин по скриншотам — гадание: не видно ни имён параметров,
    /// ни того, где лежат помещения, ни чем набиты легенды. Отчёт снимает это одним прогоном:
    /// какие категории заполнены, как названы параметры, есть ли связи с помещениями, что внутри
    /// легенд и какие типы текста доступны.
    ///
    /// Отчёт только читает модель и ничего в ней не меняет.
    /// </summary>
    internal static class ModelReport
    {
        private const int MaxTypeRows = 400;
        private const int MaxParameterValue = 160;
        private const int SampleInstances = 12;

        public static string Write(Document document)
        {
            var text = new StringBuilder();

            Header(text, document);
            Levels(text, document);
            Categories(text, document);
            Types(text, document);
            ParameterSamples(text, document);
            Rooms(text, document);
            Links(text, document);
            Legends(text, document);
            TextTypes(text, document);
            LineStyles(text, document);
            SchemeViews(text, document);
            SchemeContents(text, document);
            Circuits(text, document);
            Zones(text, document);
            Instances(text, document);

            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SchemeBuilder");

            Directory.CreateDirectory(directory);

            string path = Path.Combine(
                directory,
                "analysis-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");

            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static void Header(StringBuilder text, Document document)
        {
            Section(text, "ДОКУМЕНТ");

            text.AppendLine("Имя: " + document.Title);
            text.AppendLine("Путь: " + (string.IsNullOrEmpty(document.PathName) ? "(не сохранён)" : document.PathName));
            text.AppendLine("Версия: " + document.Application.VersionName + " " + document.Application.VersionBuild);
            text.AppendLine("Совместная работа: " + (document.IsWorkshared ? "да" : "нет"));
            text.AppendLine("Отсоединён: " + (document.IsDetached ? "да" : "нет"));
            text.AppendLine("Единицы длины: " +
                            document.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId);
            text.AppendLine();
        }

        private static void Levels(StringBuilder text, Document document)
        {
            Section(text, "УРОВНИ");

            List<Level> levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            text.AppendLine("Всего: " + levels.Count.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("отметка, мм\tимя");

            foreach (Level level in levels)
            {
                text.AppendLine(Millimeters(level.Elevation) + "\t" + level.Name);
            }

            text.AppendLine();
        }

        private static void Categories(StringBuilder text, Document document)
        {
            Section(text, "КАТЕГОРИИ (кандидаты раздела)");
            text.AppendLine("категория\tэкземпляров\tтипов");

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                List<Element> instances = Instances(document, category).ToList();
                int types = instances.Select(e => e.GetTypeId().IntValue()).Distinct().Count();

                text.AppendLine(Label(category) + "\t" +
                                instances.Count.ToString(CultureInfo.InvariantCulture) + "\t" +
                                types.ToString(CultureInfo.InvariantCulture));
            }

            text.AppendLine();
        }

        private static void Types(StringBuilder text, Document document)
        {
            Section(text, "ТИПЫ ОБОРУДОВАНИЯ");
            text.AppendLine("Марки показаны примерами: по ним видно, годится ли префикс как код УГО.");
            text.AppendLine("категория\tсемейство\tтип\tшт\tмарки (примеры)\tADSK_Наименование");

            int written = 0;

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                var byType = new Dictionary<int, List<Element>>();

                foreach (Element instance in Instances(document, category))
                {
                    int typeId = instance.GetTypeId().IntValue();
                    if (!byType.TryGetValue(typeId, out List<Element> list))
                    {
                        list = new List<Element>();
                        byType[typeId] = list;
                    }

                    list.Add(instance);
                }

                foreach (KeyValuePair<int, List<Element>> pair in byType.OrderByDescending(p => p.Value.Count))
                {
                    if (written++ >= MaxTypeRows)
                    {
                        text.AppendLine("... список обрезан");
                        text.AppendLine();
                        return;
                    }

                    var type = document.GetElement(new ElementId(pair.Key)) as ElementType;

                    string marks = string.Join(", ", pair.Value
                        .Select(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString())
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .Distinct()
                        .Take(3));

                    text.AppendLine(string.Join("\t",
                        Label(category),
                        type?.FamilyName ?? "(нет)",
                        type?.Name ?? "(нет)",
                        pair.Value.Count.ToString(CultureInfo.InvariantCulture),
                        marks,
                        Trim(type?.LookupParameter(DeviceScanner.DescriptionParameter)?.AsString())));
                }
            }

            text.AppendLine();
        }

        /// <summary>
        /// Полный список параметров у одного образца каждой категории. Именно отсюда становится
        /// видно, чем можно задать зону: секция, система, номер квартиры.
        /// </summary>
        private static void ParameterSamples(StringBuilder text, Document document)
        {
            Section(text, "ПАРАМЕТРЫ (образец на категорию)");

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                Element sample = Instances(document, category).FirstOrDefault();
                if (sample == null) continue;

                text.AppendLine("--- " + Label(category) + " --- образец ID " +
                                sample.Id.IntValue().ToString(CultureInfo.InvariantCulture));

                text.AppendLine("[экземпляр] имя\tхранение\tзначение");
                foreach (string line in ParameterLines(sample)) text.AppendLine(line);

                if (document.GetElement(sample.GetTypeId()) is ElementType type)
                {
                    text.AppendLine("[тип] имя\tхранение\tзначение");
                    foreach (string line in ParameterLines(type)) text.AppendLine(line);
                }

                text.AppendLine();
            }
        }

        private static IEnumerable<string> ParameterLines(Element element)
        {
            foreach (Parameter parameter in element.Parameters.Cast<Parameter>()
                         .OrderBy(p => p.Definition?.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                string name = parameter.Definition?.Name;
                if (string.IsNullOrEmpty(name)) continue;

                string value;
                switch (parameter.StorageType)
                {
                    case StorageType.String:
                        value = parameter.AsString();
                        break;

                    case StorageType.ElementId:
                        value = parameter.AsValueString() ?? parameter.AsElementId().IntValue()
                            .ToString(CultureInfo.InvariantCulture);
                        break;

                    default:
                        value = parameter.AsValueString();
                        break;
                }

                yield return name + "\t" + parameter.StorageType + "\t" + Trim(value);
            }
        }

        private static void Rooms(StringBuilder text, Document document)
        {
            Section(text, "ПОМЕЩЕНИЯ В ЭТОЙ МОДЕЛИ");

            List<Element> rooms = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList();

            text.AppendLine("Всего: " + rooms.Count.ToString(CultureInfo.InvariantCulture));

            if (rooms.Count > 0)
            {
                text.AppendLine("Параметры образца помещения:");
                text.AppendLine("имя\tхранение\tзначение");
                foreach (string line in ParameterLines(rooms[0])) text.AppendLine(line);
            }

            text.AppendLine();
        }

        /// <summary>
        /// Связи — главный вопрос по зонам: помещения ведёт АР, и надо понять, подключён ли он
        /// и заполнены ли в нём помещения.
        /// </summary>
        private static void Links(StringBuilder text, Document document)
        {
            Section(text, "СВЯЗИ RVT");

            List<RevitLinkInstance> links = new FilteredElementCollector(document)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .ToList();

            text.AppendLine("Экземпляров связей: " + links.Count.ToString(CultureInfo.InvariantCulture));

            foreach (RevitLinkInstance link in links)
            {
                Document linked = link.GetLinkDocument();

                if (linked == null)
                {
                    text.AppendLine(link.Name + "\tне загружена (документ недоступен)");
                    continue;
                }

                int rooms = new FilteredElementCollector(linked)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .GetElementCount();

                text.AppendLine(link.Name + "\tзагружена\tпомещений: " +
                                rooms.ToString(CultureInfo.InvariantCulture) + "\t" + linked.Title);

                if (rooms == 0) continue;

                Element sample = new FilteredElementCollector(linked)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .FirstElement();

                if (sample == null) continue;

                text.AppendLine("  параметры образца помещения из связи:");
                foreach (string line in ParameterLines(sample)) text.AppendLine("  " + line);
            }

            text.AppendLine();
        }

        /// <summary>Что внутри легенд: есть ли образец компонента и чем таблица набита сейчас.</summary>
        private static void Legends(StringBuilder text, Document document)
        {
            Section(text, "ЛЕГЕНДЫ");
            text.AppendLine("имя\tмасштаб\tвсего элементов\tкомпонентов легенды\tтекстов\tлиний\tсимволов");

            foreach (View legend in LegendBuilder.FindLegends(document))
            {
                List<Element> elements = new FilteredElementCollector(document, legend.Id)
                    .WhereElementIsNotElementType()
                    .ToList();

                int components = elements.Count(e => e.Category?.Id.IntValue() ==
                                                     (int)BuiltInCategory.OST_LegendComponents);
                int texts = elements.Count(e => e is TextNote);
                int lines = elements.Count(e => e is DetailCurve);
                int symbols = elements.Count(e => e is AnnotationSymbol);

                text.AppendLine(string.Join("\t",
                    legend.Name,
                    "1:" + legend.Scale.ToString(CultureInfo.InvariantCulture),
                    elements.Count.ToString(CultureInfo.InvariantCulture),
                    components.ToString(CultureInfo.InvariantCulture),
                    texts.ToString(CultureInfo.InvariantCulture),
                    lines.ToString(CultureInfo.InvariantCulture),
                    symbols.ToString(CultureInfo.InvariantCulture)));
            }

            text.AppendLine();
        }

        private static void TextTypes(StringBuilder text, Document document)
        {
            Section(text, "ТИПЫ ТЕКСТА");
            text.AppendLine("имя\tвысота текста, мм\tшрифт");

            ElementId defaultType = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

            foreach (TextNoteType type in new FilteredElementCollector(document)
                         .OfClass(typeof(TextNoteType))
                         .Cast<TextNoteType>()
                         .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                double size = type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0.0;
                string font = type.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString() ?? string.Empty;

                text.AppendLine(type.Name + (type.Id == defaultType ? " (по умолчанию)" : string.Empty) +
                                "\t" + Millimeters(size) + "\t" + font);
            }

            text.AppendLine();
        }

        private static void SchemeViews(StringBuilder text, Document document)
        {
            Section(text, "ВИДЫ СО СЛОВОМ «СХЕМА»");
            text.AppendLine("имя\tтип вида\tмасштаб\tэлементов");

            foreach (View view in new FilteredElementCollector(document)
                         .OfClass(typeof(View))
                         .Cast<View>()
                         .Where(v => !v.IsTemplate && v.Name.IndexOf("схем", StringComparison.CurrentCultureIgnoreCase) >= 0))
            {
                int count;
                try
                {
                    count = new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType().GetElementCount();
                }
                catch (Exception)
                {
                    count = -1;
                }

                text.AppendLine(string.Join("\t",
                    view.Name,
                    view.ViewType.ToString(),
                    "1:" + view.Scale.ToString(CultureInfo.InvariantCulture),
                    count.ToString(CultureInfo.InvariantCulture)));
            }

            text.AppendLine();
        }

        /// <summary>
        /// Стили и шаблоны линий: магистрали схемы разноцветные и разной штриховки, и рисовать
        /// их нечем, пока неизвестно, что заведено в проекте.
        /// </summary>
        private static void LineStyles(StringBuilder text, Document document)
        {
            Section(text, "СТИЛИ ЛИНИЙ");

            Category lines = document.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines != null)
            {
                text.AppendLine("имя\tцвет R,G,B\tвес");

                foreach (Category style in lines.SubCategories.Cast<Category>()
                             .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                {
                    Autodesk.Revit.DB.Color color = style.LineColor;
                    string rgb = color != null && color.IsValid
                        ? color.Red + "," + color.Green + "," + color.Blue
                        : string.Empty;

                    int weight = style.GetLineWeight(GraphicsStyleType.Projection) ?? 0;
                    text.AppendLine(style.Name + "\t" + rgb + "\t" + weight.ToString(CultureInfo.InvariantCulture));
                }
            }

            text.AppendLine();
            text.AppendLine("Шаблоны линий:");

            foreach (LinePatternElement pattern in new FilteredElementCollector(document)
                         .OfClass(typeof(LinePatternElement))
                         .Cast<LinePatternElement>()
                         .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                text.AppendLine("  " + pattern.Name);
            }

            text.AppendLine();
        }

        /// <summary>
        /// Разбор уже начерченной схемы — готовый рецепт оформления: какими семействами ставятся
        /// блоки, какими стилями рисуются магистрали, каким текстом подписи и с каким шагом
        /// разложена сетка. Без него оформление пришлось бы выдумывать заново.
        /// </summary>
        private static void SchemeContents(StringBuilder text, Document document)
        {
            Section(text, "СОСТАВ НАЧЕРЧЕННЫХ СХЕМ");

            List<View> views = new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.Name.IndexOf("схем", StringComparison.CurrentCultureIgnoreCase) >= 0)
                .ToList();

            foreach (View view in views)
            {
                List<Element> elements;
                try
                {
                    elements = new FilteredElementCollector(document, view.Id)
                        .WhereElementIsNotElementType()
                        .ToList();
                }
                catch (Exception)
                {
                    continue;
                }

                if (elements.Count == 0) continue;

                text.AppendLine("--- " + view.Name + " (" + view.ViewType + ", 1:" +
                                view.Scale.ToString(CultureInfo.InvariantCulture) + ", элементов: " +
                                elements.Count.ToString(CultureInfo.InvariantCulture) + ")");

                text.AppendLine("  по категориям:");
                foreach (IGrouping<string, Element> group in elements
                             .GroupBy(e => e.Category?.Name ?? "(без категории)")
                             .OrderByDescending(g => g.Count()))
                {
                    text.AppendLine("    " + group.Key + "\t" + group.Count().ToString(CultureInfo.InvariantCulture));
                }

                text.AppendLine("  семейства (блоки):");
                foreach (IGrouping<string, FamilyInstance> group in elements.OfType<FamilyInstance>()
                             .GroupBy(f => f.Symbol?.FamilyName + ": " + f.Symbol?.Name)
                             .OrderByDescending(g => g.Count())
                             .Take(40))
                {
                    text.AppendLine("    " + group.Key + "\t" + group.Count().ToString(CultureInfo.InvariantCulture));
                }

                text.AppendLine("  линии по стилям:");
                foreach (IGrouping<string, DetailCurve> group in elements.OfType<DetailCurve>()
                             .GroupBy(c => c.LineStyle?.Name ?? "(без стиля)")
                             .OrderByDescending(g => g.Count()))
                {
                    text.AppendLine("    " + group.Key + "\t" + group.Count().ToString(CultureInfo.InvariantCulture));
                }

                text.AppendLine("  тексты по типам:");
                foreach (IGrouping<string, TextNote> group in elements.OfType<TextNote>()
                             .GroupBy(t => (document.GetElement(t.GetTypeId()) as ElementType)?.Name ?? "?")
                             .OrderByDescending(g => g.Count()))
                {
                    text.AppendLine("    " + group.Key + "\t" + group.Count().ToString(CultureInfo.InvariantCulture));
                }

                // Координаты блоков: по ним восстанавливается шаг сетки и границы полос этажей.
                List<XYZ> points = elements.OfType<FamilyInstance>()
                    .Select(f => (f.Location as LocationPoint)?.Point)
                    .Where(p => p != null)
                    .ToList();

                if (points.Count > 0)
                {
                    text.AppendLine("  габарит блоков, мм: X " + Millimeters(points.Min(p => p.X)) + " … " +
                                    Millimeters(points.Max(p => p.X)) + ";  Y " +
                                    Millimeters(points.Min(p => p.Y)) + " … " + Millimeters(points.Max(p => p.Y)));

                    text.AppendLine("  первые координаты блоков (X, Y мм):");
                    foreach (XYZ point in points.OrderBy(p => -p.Y).ThenBy(p => p.X).Take(30))
                    {
                        text.AppendLine("    " + Millimeters(point.X) + "\t" + Millimeters(point.Y));
                    }
                }

                text.AppendLine();
            }

            text.AppendLine();
        }

        /// <summary>
        /// Электрические цепи. Если оборудование в них объединено, связи между блоками схемы
        /// можно брать из модели, а не расставлять руками.
        /// </summary>
        private static void Circuits(StringBuilder text, Document document)
        {
            Section(text, "ЭЛЕКТРИЧЕСКИЕ ЦЕПИ");

            List<Element> systems = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.Electrical.ElectricalSystem))
                .ToList();

            text.AppendLine("Всего цепей: " + systems.Count.ToString(CultureInfo.InvariantCulture));

            foreach (Element system in systems.Take(20))
            {
                var circuit = (Autodesk.Revit.DB.Electrical.ElectricalSystem)system;

                int count;
                try
                {
                    count = circuit.Elements?.Size ?? 0;
                }
                catch (Exception)
                {
                    count = -1;
                }

                text.AppendLine(circuit.Name + "\tэлементов: " + count.ToString(CultureInfo.InvariantCulture) +
                                "\tщит: " + (circuit.PanelName ?? string.Empty));
            }

            text.AppendLine();
        }

        /// <summary>
        /// Зоны по сохранённым настройкам, с координатами. Средний X зоны задаёт порядок колонок
        /// будущей матрицы: на выпущенных схемах они идут не по алфавиту, а слева направо по плану.
        /// </summary>
        private static void Zones(StringBuilder text, Document document)
        {
            Section(text, "ЗОНЫ ПО ТЕКУЩИМ НАСТРОЙКАМ");

            SchemeSettings settings = SettingsStorage.Read(document);
            List<DeviceRow> rows = DeviceScanner.Scan(document, settings.Categories, settings);

            // Координаты собираются попутно: матрице они не нужны, а порядок колонок по ним
            // и восстанавливается — на выпущенных схемах зоны идут слева направо по плану,
            // а не по алфавиту.
            var sums = new Dictionary<string, XYZ>();
            var counts = new Dictionary<string, int>();

            SchemeData data = SchemeScanner.Scan(document, settings, rows, (floor, zone, point) =>
            {
                if (point == null) return;

                string key = floor + "|" + zone;
                sums[key] = sums.TryGetValue(key, out XYZ sum) ? sum + point : point;
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            });

            text.AppendLine("Зонирование: " + settings.Zoning +
                            (string.IsNullOrWhiteSpace(settings.ZoneParameterName)
                                ? string.Empty
                                : " по параметру «" + settings.ZoneParameterName + "»"));

            foreach (string note in data.Notes) text.AppendLine(note);

            text.AppendLine("Разложено: " + data.Placed.ToString(CultureInfo.InvariantCulture) +
                            ", без зоны: " + data.WithoutZone.ToString(CultureInfo.InvariantCulture) +
                            ", без уровня: " + data.WithoutLevel.ToString(CultureInfo.InvariantCulture));

            text.AppendLine("этаж\tотметка, мм\tзона\tустройств\tсредний X, мм\tсредний Y, мм");

            foreach (FloorGroup floor in data.Floors)
            {
                foreach (ZoneGroup zone in floor.Zones)
                {
                    string key = floor.Name + "|" + zone.Name;
                    XYZ center = sums.TryGetValue(key, out XYZ sum) ? sum / counts[key] : null;

                    text.AppendLine(string.Join("\t",
                        floor.Name,
                        Millimeters(floor.Elevation),
                        zone.Name,
                        zone.Total.ToString(CultureInfo.InvariantCulture),
                        center == null ? string.Empty : Millimeters(center.X),
                        center == null ? string.Empty : Millimeters(center.Y)));
                }
            }

            text.AppendLine();
        }

        /// <summary>
        /// Несколько живых экземпляров целиком: уровень, координаты и найденное помещение.
        /// По ним видно, срабатывает ли поиск помещения и на какой отметке стоят устройства.
        /// </summary>
        private static void Instances(StringBuilder text, Document document)
        {
            Section(text, "ПРИМЕРЫ ЭКЗЕМПЛЯРОВ");

            var locator = new RoomLocator(document);
            text.AppendLine("Источники помещений: " +
                            (locator.HasSources ? string.Join(", ", locator.SourceNames) : "нет"));
            text.AppendLine("id\tкатегория\tсемейство: тип\tмарка\tуровень\tX, мм\tY, мм\tZ, мм\tпомещение");

            int written = 0;

            foreach (BuiltInCategory category in DeviceScanner.KnownCategories)
            {
                foreach (Element instance in Instances(document, category).Take(3))
                {
                    if (written++ >= SampleInstances)
                    {
                        text.AppendLine();
                        return;
                    }

                    var type = document.GetElement(instance.GetTypeId()) as ElementType;
                    XYZ point = (instance.Location as LocationPoint)?.Point;

                    ElementId levelId = instance.LevelId;
                    var level = document.GetElement(levelId) as Level;

                    string room = "(нет точки)";
                    if (point != null)
                    {
                        Room found = locator.Find(point, level?.Elevation ?? 0.0);
                        room = found == null
                            ? "не найдено"
                            : (found.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "(без имени)") +
                              " / " + (found.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty);
                    }

                    text.AppendLine(string.Join("\t",
                        instance.Id.IntValue().ToString(CultureInfo.InvariantCulture),
                        Label(category),
                        (type?.FamilyName ?? "?") + ": " + (type?.Name ?? "?"),
                        instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? string.Empty,
                        level?.Name ?? "(нет)",
                        point == null ? string.Empty : Millimeters(point.X),
                        point == null ? string.Empty : Millimeters(point.Y),
                        point == null ? string.Empty : Millimeters(point.Z),
                        room));
                }
            }

            text.AppendLine();
        }

        private static IEnumerable<Element> Instances(Document document, BuiltInCategory category)
        {
            return new FilteredElementCollector(document)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() != ElementId.InvalidElementId);
        }

        private static void Section(StringBuilder text, string title)
        {
            text.AppendLine("=== " + title + " ===");
        }

        private static string Label(BuiltInCategory category)
        {
            try
            {
                return LabelUtils.GetLabelFor(category);
            }
            catch (Exception)
            {
                return category.ToString();
            }
        }

        private static string Millimeters(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters)
                .ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string single = value.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            return single.Length <= MaxParameterValue ? single : single.Substring(0, MaxParameterValue) + "…";
        }
    }
}
