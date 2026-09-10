using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Блок ячейки: код, УГО и количество — «BTH / 3 шт.».</summary>
    internal class MatrixBlock
    {
        public MatrixBlock(ElementId typeId, string code, string typeName, int count, string loop)
        {
            TypeId = typeId;
            Code = code;
            TypeName = typeName;
            Count = count;
            Loop = loop ?? string.Empty;
        }

        public ElementId TypeId { get; }

        public string Code { get; }

        public string TypeName { get; }

        public int Count { get; }

        /// <summary>Шлейф, в котором сидят эти приборы: «1/2». Пусто — шлейф из марки не читается.</summary>
        public string Loop { get; }

        /// <summary>Подпись под блоком: «3 шт.».</summary>
        public string Amount => Count.ToString(CultureInfo.CurrentCulture) + " шт.";

        /// <summary>Подпись над блоком. Без кода подписываем типом — иначе блок немой.</summary>
        public string Caption => string.IsNullOrWhiteSpace(Code) ? TypeName : Code;
    }

    /// <summary>
    /// Ячейка схемы — одно помещение этажа: заголовок с именем и ряд блоков под ним.
    /// Блоков много — они переносятся на вторую строку внутри той же ячейки, как в подвале
    /// выпущенного листа.
    /// </summary>
    internal class MatrixCell
    {
        public MatrixCell(string key, string title)
        {
            Key = key;
            Title = title;
        }

        public string Key { get; }

        public string Title { get; }

        public List<MatrixBlock> Blocks { get; } = new List<MatrixBlock>();

        /// <summary>Блоков в одной строке ячейки.</summary>
        public int PerLine { get; set; } = 1;

        /// <summary>Строк блоков в ячейке.</summary>
        public int Lines { get; set; } = 1;

        /// <summary>Ширина на листе, мм. Считается по числу блоков и растягивается до общей ширины.</summary>
        public double WidthMm { get; set; }

        public bool IsEmpty => Blocks.Count == 0;

        public int Total => Blocks.Sum(b => b.Count);
    }

    /// <summary>
    /// Строка матрицы. Это не обязательно один этаж: типовые этажи с одинаковым составом
    /// сворачиваются в одну строку «2–16 этаж (тип.)».
    /// </summary>
    internal class MatrixBand
    {
        /// <summary>Этажи строки сверху вниз. Их больше одного только у типовой строки.</summary>
        public List<FloorGroup> Floors { get; } = new List<FloorGroup>();

        /// <summary>Подпись слева: «6 этаж на отм. +15,600».</summary>
        public string Title { get; set; } = string.Empty;

        public string Elevation { get; set; } = string.Empty;

        public bool IsTypical => Floors.Count > 1;

        /// <summary>Ячейки слева направо — в том порядке, в каком помещения идут по зданию.</summary>
        public List<MatrixCell> Cells { get; } = new List<MatrixCell>();

        /// <summary>
        /// Сколько ячеек стоит слева от колонки стояков. Стояк обязан быть вертикальным, поэтому
        /// делится не число ячеек, а ширина: слева от стояков у каждой строки своя половина.
        /// </summary>
        public int SplitIndex { get; set; }

        /// <summary>Строк блоков в самой высокой ячейке строки.</summary>
        public int Lines => Cells.Count == 0 ? 1 : Cells.Max(c => c.Lines);

        public int Total => Cells.Sum(c => c.Total);

        public string Caption =>
            string.IsNullOrWhiteSpace(Elevation) ? Title : Title + " на отм. " + Elevation;
    }

    /// <summary>
    /// Матрица схемы, разложенная арифметически: строки-этажи сверху вниз, в строке — ячейки
    /// помещений слева направо.
    ///
    /// Сетка не сквозная: у подвала свои помещения, у типового этажа свои, и делить их общими
    /// колонками нельзя — на выпущенном листе каждая строка поделена по-своему, а строки
    /// растянуты до общей ширины.
    /// </summary>
    internal class MatrixLayout
    {
        public List<MatrixBand> Bands { get; } = new List<MatrixBand>();

        public List<string> Notes { get; } = new List<string>();

        /// <summary>Ширина схемы на листе без левой колонки с этажами, мм.</summary>
        public double BodyWidthMm { get; set; }

        public int Cells => Bands.Sum(b => b.Cells.Count);

        public int Devices => Bands.Sum(b => b.Total);

        /// <param name="collapseTypical">
        /// Сворачивать ли подряд идущие этажи с одинаковым составом в одну строку. Сравнивается
        /// именно состав (помещения, типы, количества), а не имя этажа: «типовой» на схеме значит
        /// «выглядит так же», и расхождение в одном устройстве обязано разорвать диапазон.
        /// </param>
        public static MatrixLayout Build(SchemeData data, SchemeSettings settings, bool collapseTypical)
        {
            var layout = new MatrixLayout();
            if (data == null) return layout;

            foreach (FloorGroup floor in data.Floors)
            {
                MatrixBand last = layout.Bands.LastOrDefault();

                if (collapseTypical && last != null && Signature(last.Floors[0]) == Signature(floor))
                {
                    last.Floors.Add(floor);
                    continue;
                }

                var band = new MatrixBand();
                band.Floors.Add(floor);
                layout.Bands.Add(band);
            }

            int perLineLimit = Math.Max(1, settings.MatrixBlocksPerLine);

            foreach (MatrixBand band in layout.Bands)
            {
                band.Title = TitleOf(band);
                band.Elevation = ElevationOf(band);

                // Ячейки берутся с первого этажа строки: у остальных состав тот же, иначе они бы
                // в неё не попали.
                foreach (ZoneGroup zone in Order(band.Floors[0].Zones, settings))
                {
                    // Ячейке «зона не определена» на листе не место: это брак данных, а не
                    // помещение. Считать её надо — потому и остаётся выключателем, — но по
                    // умолчанию она не чертится, иначе на каждом этаже зияет колонка без имени.
                    if (!settings.MatrixShowUnzoned && zone.Key == SchemeScanner.NoZone) continue;

                    var cell = new MatrixCell(zone.Key, zone.Name);

                    foreach (DeviceCount device in zone.Devices)
                    {
                        cell.Blocks.Add(new MatrixBlock(device.TypeId, device.Code, device.TypeName, device.Count, device.Loop));
                    }

                    // Ряд блоков рвётся на равные строки, а не «двенадцать и один»: одинокий блок
                    // во второй строке выглядит браком вёрстки.
                    int blocks = Math.Max(1, cell.Blocks.Count);
                    cell.Lines = (blocks + perLineLimit - 1) / perLineLimit;
                    cell.PerLine = (blocks + cell.Lines - 1) / cell.Lines;
                    cell.WidthMm = Math.Max(settings.MatrixCellMinMm, cell.PerLine * settings.MatrixBlockMm);

                    band.Cells.Add(cell);
                }
            }

            Stretch(layout, settings);

            int typical = layout.Bands.Count(b => b.IsTypical);
            if (typical > 0)
            {
                layout.Notes.Add("Типовых строк: " + typical.ToString(CultureInfo.CurrentCulture) +
                                 " — этажи с одинаковым составом сведены в одну.");
            }

            int unzoned = data.Floors
                .SelectMany(f => f.Zones)
                .Where(z => z.Key == SchemeScanner.NoZone)
                .Sum(z => z.Total);

            if (unzoned > 0)
            {
                layout.Notes.Add(settings.MatrixShowUnzoned
                    ? "Устройств без помещения: " + unzoned.ToString(CultureInfo.CurrentCulture) +
                      " — они собраны в ячейки «" + SchemeScanner.NoZone + "»."
                    : "Устройств без помещения: " + unzoned.ToString(CultureInfo.CurrentCulture) +
                      " — НА СХЕМУ НЕ ПОПАЛИ. Их видно на шаге 3; чтобы начертить, включите " +
                      "«показывать зону не определена».");
            }

            return layout;
        }

        /// <summary>
        /// Порядок ячеек в строке. По координате он повторяет порядок помещений по зданию —
        /// именно так колонки идут на выпущенных листах, и из имён этого не вывести.
        ///
        /// Ось задаётся руками: у коридорного дома квартиры стоят по обе стороны, и сортировка
        /// по одной координате их перемешивает — квартира с одной стороны встаёт между двумя
        /// с другой. Тогда берут «стороны раздельно».
        /// </summary>
        private static IEnumerable<ZoneGroup> Order(IEnumerable<ZoneGroup> zones, SchemeSettings settings)
        {
            List<ZoneGroup> list = zones.ToList();
            List<ZoneGroup> sorted;

            if (settings.MatrixOrder == ZoneOrder.ByName)
            {
                sorted = list.OrderBy(z => z.Name, NaturalComparer.Instance).ToList();
            }
            else
            {
                bool sides = settings.MatrixOrder == ZoneOrder.SideThenX ||
                             settings.MatrixOrder == ZoneOrder.SideThenY;

                bool alongX = settings.MatrixOrder == ZoneOrder.AlongX ||
                              settings.MatrixOrder == ZoneOrder.SideThenX;

                // Сторона считается от середины этажа поперёк дома, а не от нуля проекта:
                // координаты площадки бывают любыми.
                double middle = Middle(list, !alongX);

                sorted = list
                    // Зоны без координат — в конец: сравнивать их с чертёжным порядком не по чему.
                    .OrderBy(z => z.HasPosition ? 0 : 1)
                    .ThenBy(z => sides ? Side(z, alongX, middle) : 0)
                    .ThenBy(z => Along(z, alongX))
                    .ThenBy(z => z.Name, NaturalComparer.Instance)
                    .ToList();
            }

            if (settings.MatrixOrderReversed) sorted.Reverse();
            return sorted;
        }

        private static double Along(ZoneGroup zone, bool alongX)
        {
            if (!zone.HasPosition) return 0.0;
            return alongX ? zone.AverageX : zone.AverageY;
        }

        private static int Side(ZoneGroup zone, bool alongX, double middle)
        {
            if (!zone.HasPosition) return 0;

            double across = alongX ? zone.AverageY : zone.AverageX;
            return across >= middle ? 0 : 1;
        }

        /// <summary>Середина этажа поперёк дома — по ней зоны делятся на стороны коридора.</summary>
        private static double Middle(List<ZoneGroup> zones, bool byX)
        {
            List<double> values = zones
                .Where(z => z.HasPosition)
                .Select(z => byX ? z.AverageX : z.AverageY)
                .OrderBy(v => v)
                .ToList();

            if (values.Count == 0) return 0.0;

            // Медиана, а не среднее: одна дальняя кладовка не должна сдвигать границу сторон.
            return values[values.Count / 2];
        }

        /// <summary>
        /// Строки растягиваются до общей ширины: у подвала помещений втрое меньше, чем у типового
        /// этажа, а правый край схемы должен быть один. Растягиваются пропорционально — ячейка
        /// с пятью блоками остаётся шире ячейки с одним.
        /// </summary>
        private static void Stretch(MatrixLayout layout, SchemeSettings settings)
        {
            double widest = 0.0;

            foreach (MatrixBand band in layout.Bands)
            {
                widest = Math.Max(widest, band.Cells.Sum(c => c.WidthMm));
            }

            layout.BodyWidthMm = widest;
            if (widest <= 0.0) return;

            bool middle = settings.MatrixDrawTrunks && settings.MatrixTrunkPosition == TrunkPosition.Middle;

            foreach (MatrixBand band in layout.Bands)
            {
                double own = band.Cells.Sum(c => c.WidthMm);
                if (own <= 0.0) continue;

                if (!middle)
                {
                    double factor = widest / own;
                    foreach (MatrixCell cell in band.Cells) cell.WidthMm *= factor;
                    band.SplitIndex = band.Cells.Count;
                    continue;
                }

                Split(band, own, widest);
            }
        }

        /// <summary>
        /// Делит строку надвое по её же ширине и растягивает половины до половин схемы. Так стояк
        /// посередине остаётся вертикальным при любом числе ячеек в строке: у подвала их шесть,
        /// у типового этажа четырнадцать, а граница половин у всех на одной вертикали.
        /// </summary>
        private static void Split(MatrixBand band, double own, double widest)
        {
            double half = own / 2.0;
            double run = 0.0;
            int index = 0;

            while (index < band.Cells.Count && run + band.Cells[index].WidthMm / 2.0 < half)
            {
                run += band.Cells[index].WidthMm;
                index++;
            }

            // Пустая половина растянула бы единственную ячейку на всю схему — тогда делим поровну.
            if (index == 0) index = 1;
            if (index >= band.Cells.Count) index = band.Cells.Count - 1;

            band.SplitIndex = index;

            double left = band.Cells.Take(index).Sum(c => c.WidthMm);
            double right = own - left;

            for (int cell = 0; cell < band.Cells.Count; cell++)
            {
                double part = cell < index ? left : right;
                if (part <= 0.0) continue;

                band.Cells[cell].WidthMm *= widest / 2.0 / part;
            }
        }

        /// <summary>Состав этажа строкой: по нему решается, типовой ли он.</summary>
        private static string Signature(FloorGroup floor)
        {
            var text = new StringBuilder();

            foreach (ZoneGroup zone in floor.Zones.OrderBy(z => z.Name, NaturalComparer.Instance))
            {
                // В ключ помещения входит его идентификатор, а он у каждого этажа свой — сравнивать
                // этажи можно только по именам зон и составу.
                text.Append(zone.Name).Append('{');

                foreach (DeviceCount device in zone.Devices.OrderBy(d => d.TypeId.IntegerValue))
                {
                    text.Append(device.TypeId.IntegerValue.ToString(CultureInfo.InvariantCulture))
                        .Append(':')
                        .Append(device.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(';');
                }

                text.Append('}');
            }

            return text.ToString();
        }

        private static string TitleOf(MatrixBand band)
        {
            if (!band.IsTypical) return FloorName(band.Floors[0].Name);

            // Этажи в строке идут сверху вниз, а диапазон подписывают снизу вверх: «Этаж 2–16».
            string lower = FloorName(band.Floors[band.Floors.Count - 1].Name);
            string upper = FloorName(band.Floors[0].Name);

            return Range(lower, upper) + " (тип.)";
        }

        /// <summary>
        /// Имя этажа для подписи. Уровни в модели названы «06 15,600 Этаж 6» и «-01 -3,250 Подвал»:
        /// номер и отметка идут перед именем, а отметка на схеме пишется своя, рядом. Поэтому
        /// ведущие числа отбрасываются и остаётся «Этаж 6», «Подвал».
        /// </summary>
        public static string FloorName(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName)) return string.Empty;

            string[] parts = levelName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            int start = 0;
            while (start < parts.Length && IsNumber(parts[start])) start++;

            // Всё имя оказалось числами — значит другого имени у уровня нет, отдаём как есть.
            if (start >= parts.Length) return levelName.Trim();

            return string.Join(" ", parts.Skip(start));
        }

        private static bool IsNumber(string token)
        {
            bool digits = false;

            foreach (char symbol in token)
            {
                if (char.IsDigit(symbol))
                {
                    digits = true;
                    continue;
                }

                if (symbol != ',' && symbol != '.' && symbol != '-' && symbol != '+') return false;
            }

            return digits;
        }

        /// <summary>
        /// «Этаж 2» и «Этаж 16» → «Этаж 2–16»; «2 этаж» и «16 этаж» → «2–16 этаж». Если имена
        /// устроены иначе — оба через тире, как есть: придумывать за пользователя нумерацию этажей
        /// нельзя, а показать диапазон надо.
        /// </summary>
        private static string Range(string lower, string upper)
        {
            Split(lower, out string lowerHead, out string lowerNumber, out string lowerTail);
            Split(upper, out string upperHead, out string upperNumber, out string upperTail);

            bool sameFrame = lowerNumber != null && upperNumber != null &&
                             string.Equals(lowerHead, upperHead, StringComparison.CurrentCultureIgnoreCase) &&
                             string.Equals(lowerTail, upperTail, StringComparison.CurrentCultureIgnoreCase);

            if (sameFrame) return lowerHead + lowerNumber + "–" + upperNumber + lowerTail;

            return lower + " – " + upper;
        }

        /// <summary>Разбирает имя на «до числа», «число» и «после числа». Числа нет — вернёт null.</summary>
        private static void Split(string name, out string head, out string number, out string tail)
        {
            head = name;
            number = null;
            tail = string.Empty;

            if (string.IsNullOrWhiteSpace(name)) return;

            int first = -1;
            for (int i = 0; i < name.Length; i++)
            {
                if (!char.IsDigit(name[i])) continue;

                first = i;
                break;
            }

            if (first < 0) return;

            int last = first;
            while (last + 1 < name.Length && char.IsDigit(name[last + 1])) last++;

            head = name.Substring(0, first);
            number = name.Substring(first, last - first + 1);
            tail = name.Substring(last + 1);
        }

        private static string ElevationOf(MatrixBand band)
        {
            string top = Meters(band.Floors[0].Elevation);
            if (!band.IsTypical) return top;

            string bottom = Meters(band.Floors[band.Floors.Count - 1].Elevation);
            return bottom + "…" + top;
        }

        /// <summary>
        /// Отметка в метрах со знаком, как на выпущенных листах: +15,600, 0,000, -3,250.
        /// Разделитель — запятая независимо от языка системы: так набраны отметки на схеме,
        /// и точка от английской локали выглядела бы на листе чужеродно.
        /// </summary>
        public static string Meters(double internalUnits)
        {
            double meters = UnitUtils.ConvertFromInternalUnits(internalUnits, UnitTypeId.Millimeters) / 1000.0;

            var format = new NumberFormatInfo { NumberDecimalSeparator = "," };
            return meters.ToString("+0.000;-0.000;0.000", format);
        }
    }
}
