using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Раскладывает оборудование модели по этажам и зонам — это и есть содержимое будущей
    /// матрицы схемы: строка «этаж», колонка «зона», в ячейке устройства с количествами.
    /// </summary>
    internal static class SchemeScanner
    {
        public const string NoZone = "— зона не определена —";

        /// <param name="observer">
        /// Необязательный наблюдатель «этаж, зона, точка устройства». Нужен отчёту, чтобы
        /// посчитать средние координаты зон, — самой матрице координаты не нужны.
        /// </param>
        public static SchemeData Scan(
            Document document,
            SchemeSettings settings,
            IList<DeviceRow> rows,
            Action<string, string, XYZ> observer = null)
        {
            var data = new SchemeData();

            Dictionary<int, string> codes = rows
                .GroupBy(r => r.TypeId.IntegerValue)
                .ToDictionary(g => g.Key, g => g.First().Code);

            // На схему идёт то же, что и в легенду. Иначе в ячейки лезут отверстия, гильзы и
            // закладные: они той же категории, и выключить их на шаге 2 оказывается бесполезно.
            var included = new HashSet<int>(rows.Where(r => r.Include).Select(r => r.TypeId.IntegerValue));

            bool byRoom = settings.Zoning != ZoneSource.DeviceParameter;
            RoomLocator locator = byRoom ? new RoomLocator(document) : null;

            if (byRoom)
            {
                data.Notes.Add(locator.HasSources
                    ? "Помещения берутся из: " + string.Join(", ", locator.SourceNames)
                    : "Помещений нет ни в этой модели, ни в связях — зоны определить не по чему.");
            }

            var floors = new Dictionary<int, FloorGroup>();
            var levelElevations = new Dictionary<int, double>();

            // Уровни снизу вверх — по ним определяется этаж приборов с незаполненным уровнем.
            List<Level> levels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            foreach (BuiltInCategory category in settings.Categories)
            {
                foreach (Element instance in new FilteredElementCollector(document)
                             .OfCategory(category)
                             .WhereElementIsNotElementType())
                {
                    ElementId typeId = instance.GetTypeId();
                    if (typeId == ElementId.InvalidElementId) continue;
                    if (!included.Contains(typeId.IntegerValue)) continue;

                    XYZ point = PointOf(instance);

                    // Этажный щит — узел схемы, а не ещё один прибор в коридоре: он становится
                    // своей ячейкой, как «ЩТС 6.1» на выпущенном листе.
                    bool panel = IsPanel(document, typeId, settings);

                    ElementId levelId = ResolveLevel(instance, point, levels);
                    if (levelId == ElementId.InvalidElementId)
                    {
                        data.WithoutLevel++;
                        continue;
                    }

                    FloorGroup floor = Floor(document, floors, levelElevations, levelId);

                    Zone place = panel
                        ? new Zone("PANEL:" + instance.UniqueId, settings.MatrixPanelPrefix.Trim())
                        : ResolveZone(instance, point, locator, settings, floor.Elevation);

                    if (string.IsNullOrWhiteSpace(place.Name))
                    {
                        place = new Zone(NoZone, NoZone);
                        data.WithoutZone++;
                    }

                    observer?.Invoke(floor.Name, place.Name, point);

                    ZoneGroup zone = floor.Zones.FirstOrDefault(z => z.Key == place.Key);
                    if (zone == null)
                    {
                        zone = new ZoneGroup(place.Key, place.Name);
                        floor.Zones.Add(zone);
                    }

                    zone.Observe(point);

                    DeviceCount device = zone.Devices.FirstOrDefault(d => d.TypeId == typeId);
                    if (device == null)
                    {
                        string code = codes.TryGetValue(typeId.IntegerValue, out string found) ? found : string.Empty;
                        string name = (document.GetElement(typeId) as ElementType)?.Name ?? string.Empty;

                        device = new DeviceCount(typeId, code, name);
                        zone.Devices.Add(device);
                    }

                    device.Count++;
                    device.Observe(Loop(instance));
                    data.Placed++;
                }
            }

            // Этажи сверху вниз, как на готовых схемах: верхний этаж — верхняя полоса.
            data.Floors.AddRange(floors.Values.OrderByDescending(f => f.Elevation));

            NumberPanels(data, settings);

            foreach (FloorGroup floor in data.Floors)
            {
                List<ZoneGroup> sorted = floor.Zones.OrderBy(z => z.Name, NaturalComparer.Instance).ToList();
                floor.Zones.Clear();
                floor.Zones.AddRange(sorted);

                foreach (ZoneGroup zone in floor.Zones)
                {
                    List<DeviceCount> devices = zone.Devices
                        .OrderBy(d => d.Code, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(d => d.TypeName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    zone.Devices.Clear();
                    zone.Devices.AddRange(devices);
                }
            }

            return data;
        }

        private static FloorGroup Floor(
            Document document,
            Dictionary<int, FloorGroup> floors,
            Dictionary<int, double> elevations,
            ElementId levelId)
        {
            int key = levelId.IntegerValue;
            if (floors.TryGetValue(key, out FloorGroup existing)) return existing;

            var level = document.GetElement(levelId) as Level;
            double elevation = level?.Elevation ?? 0.0;
            elevations[key] = elevation;

            var floor = new FloorGroup(levelId, level?.Name ?? "Уровень " + key.ToString(CultureInfo.CurrentCulture), elevation);
            floors[key] = floor;
            return floor;
        }

        /// <summary>
        /// Уровень элемента. Сначала штатное свойство, затем параметры: у части семейств уровень
        /// живёт только в «уровне для спецификации». Если уровня нет нигде — этаж определяется
        /// по высоте точки.
        /// </summary>
        private static ElementId ResolveLevel(Element element, XYZ point, IList<Level> levels)
        {
            ElementId levelId = element.LevelId;
            if (levelId != null && levelId != ElementId.InvalidElementId) return levelId;

            BuiltInParameter[] candidates =
            {
                BuiltInParameter.FAMILY_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM,
                BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM,
                BuiltInParameter.LEVEL_PARAM,
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM
            };

            foreach (BuiltInParameter candidate in candidates)
            {
                Parameter parameter = element.get_Parameter(candidate);
                ElementId value = parameter?.AsElementId();
                if (value != null && value != ElementId.InvalidElementId) return value;
            }

            return NearestLevel(point, levels);
        }

        /// <summary>
        /// Этаж по высоте точки — ближайший уровень снизу. Так спасаются приборы, у которых уровень
        /// не заполнен ни одним параметром: их сотни, и терять их только из-за пустого поля нельзя.
        /// Прибор под потолком относится к своему этажу, а не к вышележащему, поэтому уровень
        /// ищется снизу, а не по близости.
        /// </summary>
        private static ElementId NearestLevel(XYZ point, IList<Level> levels)
        {
            if (point == null || levels.Count == 0) return ElementId.InvalidElementId;

            Level found = null;

            foreach (Level level in levels)
            {
                if (level.Elevation > point.Z + Tolerance) break;
                found = level;
            }

            // Точка ниже самого нижнего уровня — берём его же: это подвал с отрицательной отметкой
            // установки, а не «нигде».
            return (found ?? levels[0]).Id;
        }

        /// <summary>Запас на разницу отметки уровня и пола: прибор в полу не должен уехать этажом ниже.</summary>
        private const double Tolerance = 100.0 / 304.8;

        /// <summary>Ключ и имя зоны. Ключ различает соседние ячейки, имя пишется в их заголовке.</summary>
        private struct Zone
        {
            public Zone(string key, string name)
            {
                Key = key;
                Name = name;
            }

            public string Key { get; }

            public string Name { get; }
        }

        /// <summary>Этажный ли это щит — по части имени типа, заданной в настройках.</summary>
        private static bool IsPanel(Document document, ElementId typeId, SchemeSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.MatrixPanelPattern)) return false;

            ElementType type = document.GetElement(typeId) as ElementType;
            if (type == null) return false;

            return type.Name.IndexOf(settings.MatrixPanelPattern.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0
                || type.FamilyName.IndexOf(settings.MatrixPanelPattern.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        /// <summary>
        /// Нумерует щиты этажа: «ЩТС 6.1», «ЩТС 6.2» — как на выпущенном листе. Номер этажа берётся
        /// из его имени, порядок — слева направо по плану: на схеме щиты стоят в том же порядке.
        /// </summary>
        private static void NumberPanels(SchemeData data, SchemeSettings settings)
        {
            string prefix = settings.MatrixPanelPrefix ?? string.Empty;

            foreach (FloorGroup floor in data.Floors)
            {
                List<ZoneGroup> panels = floor.Zones
                    .Where(z => z.Key.StartsWith("PANEL:", StringComparison.Ordinal))
                    .OrderBy(z => z.AverageX)
                    .ToList();

                if (panels.Count == 0) continue;

                string level = FloorNumber(floor.Name);

                for (int index = 0; index < panels.Count; index++)
                {
                    string number = level.Length == 0
                        ? (index + 1).ToString(CultureInfo.CurrentCulture)
                        : level + "." + (index + 1).ToString(CultureInfo.CurrentCulture);

                    panels[index].Rename(prefix + number);
                }
            }
        }

        /// <summary>Номер этажа из его имени: «06 15,600 Этаж 6» → «6». Не нашёлся — пусто.</summary>
        private static string FloorNumber(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            // Берём последнее число имени: впереди у уровней стоят порядковый номер и отметка,
            // а номер этажа записан в конце — «06 15,600 Этаж 6».
            string digits = string.Empty;
            string found = string.Empty;

            foreach (char symbol in name)
            {
                if (char.IsDigit(symbol))
                {
                    digits += symbol;
                    continue;
                }

                if (digits.Length > 0 && symbol != ',' && symbol != '.') { found = digits; digits = string.Empty; }
                else if (symbol == ',' || symbol == '.') digits = string.Empty;
            }

            if (digits.Length > 0) found = digits;
            return found.TrimStart('0');
        }

        /// <summary>
        /// Шлейф прибора из его марки: «BTH1/2.9» → «1/2», «BIAD3.21» → «3». Буквы впереди — код,
        /// после точки — адрес в шлейфе, между ними то, что нужно: прибор и линия.
        ///
        /// Другого места под номер шлейфа в этих моделях нет — параметра под него не заведено,
        /// поэтому марка тут единственный источник, хотя как источник кода она и не годится.
        /// </summary>
        private static string Loop(Element instance)
        {
            string mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            if (string.IsNullOrWhiteSpace(mark)) return string.Empty;

            string text = mark.Trim();

            int start = 0;
            while (start < text.Length && char.IsLetter(text[start])) start++;
            if (start == 0 || start >= text.Length) return string.Empty;

            int stop = text.IndexOf('.', start);
            if (stop < 0) stop = text.Length;

            string loop = text.Substring(start, stop - start).Trim();

            // «257» без точки — это порядковый номер коробки, а не шлейф: у шлейфа есть адрес
            // после точки, и без него номер ни о чём не говорит.
            return stop == text.Length ? string.Empty : loop;
        }

        /// <summary>Точка устройства. Без неё зона не встанет на своё место в ряду колонок.</summary>
        private static XYZ PointOf(Element instance)
        {
            XYZ point = (instance.Location as LocationPoint)?.Point;
            if (point != null) return point;

            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            return box == null ? null : (box.Min + box.Max) / 2.0;
        }

        private static Zone ResolveZone(
            Element instance,
            XYZ point,
            RoomLocator locator,
            SchemeSettings settings,
            double levelElevation)
        {
            if (settings.Zoning == ZoneSource.DeviceParameter)
            {
                string value = ReadParameter(instance, settings.ZoneParameterName);
                return new Zone(value, value);
            }

            if (locator == null || !locator.HasSources || point == null) return default(Zone);

            Room room = locator.Find(point, levelElevation);
            if (room == null) return default(Zone);

            switch (settings.Zoning)
            {
                case ZoneSource.RoomNumber:
                    string number = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? string.Empty;
                    return new Zone(number, number);

                case ZoneSource.RoomParameter:
                    string parameter = ReadParameter(room, settings.ZoneParameterName);
                    return new Zone(parameter, parameter);

                case ZoneSource.ApartmentOrRoom:
                    string apartment = ReadParameter(room, settings.ApartmentParameterName);

                    // Ноль в номере квартиры значит «помещение не квартирное»: у мест общего
                    // пользования этот параметр заполнен нулём, а не пустотой.
                    if (!string.IsNullOrWhiteSpace(apartment) && apartment != "0")
                    {
                        // Квартира собирается из своих комнат: «Жилая комната», «Кухня» и «Коридор»
                        // это одна ячейка схемы, а не три.
                        return new Zone("FLAT:" + apartment, settings.ApartmentPrefix + apartment);
                    }

                    // А вот одноимённые нежилые помещения — разные ячейки: межквартирный коридор
                    // на этаже слева и справа от ядра на схеме стоит двумя ячейками, и слить их
                    // по имени значит соврать.
                    return new Zone("ROOM:" + room.UniqueId, RoomName(room));

                default:
                    string name = RoomName(room);
                    return new Zone(name, name);
            }
        }

        private static string RoomName(Room room)
        {
            return room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
        }

        private static string ReadParameter(Element element, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) return string.Empty;

            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter == null) return string.Empty;

            string value;
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    value = parameter.AsString();
                    break;

                case StorageType.Integer:
                    // У целого без единиц AsValueString пуст — номер квартиры хранится именно так.
                    value = parameter.AsValueString();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = parameter.AsInteger().ToString(CultureInfo.CurrentCulture);
                    }

                    break;

                default:
                    value = parameter.AsValueString();
                    break;
            }

            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
