using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Итог построения матрицы.</summary>
    internal class MatrixResult
    {
        public int Rows { get; set; }

        public int Cells { get; set; }

        public int Blocks { get; set; }

        /// <summary>Блоков с поставленным УГО и оставшихся текстовыми.</summary>
        public int WithUgo { get; set; }

        public int WithoutUgo { get; set; }

        /// <summary>Нарисованных шин магистралей.</summary>
        public int Trunks { get; set; }

        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// Чертит матрицу схемы в черновом виде по образцу выпущенного листа: строки-этажи сверху
    /// вниз, в строке — ячейки помещений, в ячейке имя сверху и ряд блоков под ним.
    ///
    /// Сетка не сквозная: у подвала свои помещения, у типового этажа свои. Поэтому вертикали
    /// рисуются в пределах своей строки, а насквозь идут только края схемы.
    /// </summary>
    internal static class MatrixBuilder
    {
        /// <summary>Черновой вид под схему. Вызывается внутри открытой транзакции.</summary>
        public static ViewDrafting EnsureView(Document document, SchemeSettings settings)
        {
            if (settings.MatrixViewId != ElementId.InvalidElementId &&
                document.GetElement(settings.MatrixViewId) is ViewDrafting saved && !saved.IsTemplate)
            {
                return saved;
            }

            string name = string.IsNullOrWhiteSpace(settings.MatrixViewName)
                ? "Структурная схема ПС (плагин)"
                : settings.MatrixViewName.Trim();

            List<ViewDrafting> drafting = new FilteredElementCollector(document)
                .OfClass(typeof(ViewDrafting))
                .Cast<ViewDrafting>()
                .Where(v => !v.IsTemplate)
                .ToList();

            // Вид с этим именем уже есть — значит его завёл прошлый запуск на другой машине или
            // пользователь руками. Берём его, а не плодим «имя (2)».
            ViewDrafting existing = drafting
                .FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.CurrentCultureIgnoreCase));

            if (existing != null)
            {
                settings.MatrixViewId = existing.Id;
                return existing;
            }

            ViewFamilyType type = new FilteredElementCollector(document)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting);

            if (type == null) return null;

            ViewDrafting view = ViewDrafting.Create(document, type.Id);

            try
            {
                view.Name = name;
            }
            catch (Exception)
            {
                // Имя занято видом другого типа — оставляем то, что дал Revit; вид всё равно
                // запомнится по идентификатору.
            }

            try
            {
                view.Scale = Math.Max(1, settings.MatrixScale);
            }
            catch (Exception)
            {
                // Масштаб задан шаблоном вида — не наша забота, размеры пересчитаются по факту.
            }

            settings.MatrixViewId = view.Id;
            return view;
        }

        /// <summary>Вызывается внутри открытой транзакции.</summary>
        public static MatrixResult Build(
            Document document,
            ViewDrafting view,
            MatrixLayout layout,
            SchemeSettings settings)
        {
            var result = new MatrixResult();

            Draw.RemovePrevious(document, settings.MatrixGeneratedIds);

            if (layout.Bands.Count == 0 || layout.BodyWidthMm <= 0.0)
            {
                result.Notes.Add("Раскладывать нечего: нет ни одного помещения с оборудованием.");
                return result;
            }

            ElementId textTypeId = Draw.TextTypeId(document, settings.TextTypeId);

            // Сопоставление «тип прибора → УГО» делается отдельной командой: открывать семейства
            // при каждом построении схемы слишком долго.
            Dictionary<int, ElementId> ugo = settings.MatrixDrawUgo
                ? UgoFinder.Resolve(document, settings)
                : new Dictionary<int, ElementId>();

            int scale = Math.Max(1, view.Scale);
            double floorWidth = Draw.ToModel(settings.MatrixFloorColumnMm, scale);
            double headerHeight = Draw.ToModel(settings.MatrixHeaderMm, scale);
            double blockHeight = Draw.ToModel(settings.MatrixBlockHeightMm, scale);
            double padding = Draw.ToModel(1.0, scale);

            // Стояки идут своей колонкой между подписью этажа и ячейками: вести их сквозь ячейки
            // значит перечеркнуть блоки, а вести за схемой — оторвать от неё.
            List<TrunkLine> trunks = settings.MatrixDrawTrunks
                ? TrunkLine.Used(layout)
                : new List<TrunkLine>();

            var styles = new Dictionary<string, GraphicsStyle>();
            foreach (TrunkLine trunk in trunks) styles[trunk.Name] = Draw.LineStyle(document, trunk.StyleNames);

            double trunkWidth = trunks.Count == 0 ? 0.0 : Draw.ToModel(settings.MatrixTrunkColumnMm, scale);
            TrunkPosition position = trunks.Count == 0 ? TrunkPosition.Left : settings.MatrixTrunkPosition;

            double bodyLeft = position == TrunkPosition.Left ? floorWidth + trunkWidth : floorWidth;
            double bodyWidth = Draw.ToModel(layout.BodyWidthMm, scale);
            double right = bodyLeft + bodyWidth + (position == TrunkPosition.Left ? 0.0 : trunkWidth);

            // Колонка стояков: слева от ячеек, посередине схемы или справа за последней ячейкой.
            double trunkLeft;
            switch (position)
            {
                case TrunkPosition.Middle:
                    trunkLeft = bodyLeft + bodyWidth / 2.0;
                    break;

                case TrunkPosition.Right:
                    trunkLeft = bodyLeft + bodyWidth;
                    break;

                default:
                    trunkLeft = floorWidth;
                    break;
            }

            var created = new List<ElementId>();
            double y = 0.0;

            foreach (MatrixBand band in layout.Bands)
            {
                // Полоса заголовков сначала набирается текстом и замеряется: «Лифтовой холл —
                // тамбур-шлюз» в узкой ячейке разложится на три строки, и постоянная высота
                // обрезала бы имя.
                var titles = new List<TextNote>();
                double x = bodyLeft;

                for (int index = 0; index < band.Cells.Count; index++)
                {
                    // Дойдя до середины строки, перешагиваем колонку стояков: ячейки обходят её,
                    // а не наезжают, — поэтому стояк и остаётся вертикальным на всю схему.
                    if (index == band.SplitIndex && position == TrunkPosition.Middle) x += trunkWidth;

                    MatrixCell cell = band.Cells[index];
                    double width = Draw.ToModel(cell.WidthMm, scale);

                    titles.Add(Draw.AddText(document, view, textTypeId, created, cell.Title,
                        x + width / 2.0, y - headerHeight / 2.0, Math.Max(width - 2 * padding, padding),
                        HorizontalTextAlignment.Center));

                    x += width;
                }

                double bandHeader = headerHeight;
                List<TextNote> alive = titles.Where(t => t != null).ToList();

                if (alive.Count > 0)
                {
                    document.Regenerate();
                    bandHeader = Math.Max(headerHeight, alive.Max(t => Draw.HeightOf(t, view)) + padding);

                    foreach (TextNote title in alive)
                    {
                        title.Coord = new XYZ(title.Coord.X, y - bandHeader / 2.0, 0);
                    }
                }

                double height = bandHeader + band.Lines * blockHeight;

                // Уровни шин считаются до ячеек: от каждого блока к своей шине идёт спуск,
                // и блок должен знать, куда спускаться.
                Dictionary<string, double> buses = Buses(trunks, y, height, bandHeader, scale);

                FloorLabel(document, view, textTypeId, created, band, floorWidth, y, height, padding);

                x = bodyLeft;

                for (int index = 0; index < band.Cells.Count; index++)
                {
                    if (index == band.SplitIndex && position == TrunkPosition.Middle) x += trunkWidth;

                    MatrixCell cell = band.Cells[index];
                    double width = Draw.ToModel(cell.WidthMm, scale);

                    Cell(document, view, textTypeId, created, cell, ugo, buses, styles, result,
                        x, y, width, bandHeader, blockHeight, padding);

                    // Вертикаль ставится по левой границе ячейки: правая — это левая соседки,
                    // и рисовать её второй раз значит положить линию на линию.
                    if (x > bodyLeft) Draw.AddLine(document, view, created, x, y, x, y - height);

                    x += width;
                    result.Cells++;
                    result.Blocks += cell.Blocks.Count;
                }

                Draw.AddLine(document, view, created, 0, y, right, y);

                if (trunks.Count > 0)
                {
                    Trunk(document, view, textTypeId, created, band, trunks, styles, buses, scale,
                        trunkLeft, trunkWidth, bodyLeft, right, y, height, padding, result);
                }

                y -= height;
                result.Rows++;
            }

            // Края схемы и низ последней строки — насквозь.
            Draw.AddLine(document, view, created, 0, y, right, y);
            Draw.AddLine(document, view, created, 0, 0, 0, y);
            Draw.AddLine(document, view, created, floorWidth, 0, floorWidth, y);
            Draw.AddLine(document, view, created, right, 0, right, y);

            // Границы колонки стояков — тоже насквозь: это её и делает колонкой, а не полосой,
            // случайно оказавшейся между ячейками.
            if (trunks.Count > 0 && trunkLeft > floorWidth)
            {
                Draw.AddLine(document, view, created, trunkLeft, 0, trunkLeft, y);
                Draw.AddLine(document, view, created, trunkLeft + trunkWidth, 0, trunkLeft + trunkWidth, y);
            }
            else if (trunks.Count > 0)
            {
                Draw.AddLine(document, view, created, bodyLeft, 0, bodyLeft, y);
            }

            settings.MatrixGeneratedIds.Clear();
            settings.MatrixGeneratedIds.AddRange(created.Select(id => id.IntegerValue));

            result.Notes.AddRange(layout.Notes);
            return result;
        }

        /// <summary>
        /// Магистрали строки: стояк каждой системы в своей колонке и шина по этажу от стояка
        /// до последней ячейки, где эта система есть.
        ///
        /// Это не разводка выпущенного листа — там линии разведены руками, с обходами и изломами.
        /// Здесь регулярная гребёнка: стояки вертикалями, шины горизонталями, стилями линий
        /// проекта. Состав и принадлежность верные, красота трассировки остаётся ручной.
        /// </summary>
        private static void Trunk(
            Document document,
            View view,
            ElementId textTypeId,
            List<ElementId> created,
            MatrixBand band,
            IList<TrunkLine> trunks,
            Dictionary<string, GraphicsStyle> styles,
            Dictionary<string, double> buses,
            int scale,
            double trunkLeft,
            double trunkWidth,
            double bodyLeft,
            double right,
            double top,
            double height,
            double padding,
            MatrixResult result)
        {
            double bottom = top - height;

            for (int index = 0; index < trunks.Count; index++)
            {
                TrunkLine trunk = trunks[index];
                GraphicsStyle style = styles.TryGetValue(trunk.Name, out GraphicsStyle found) ? found : null;

                // Стояк идёт через всю строку независимо от того, есть ли на этаже приборы этой
                // системы: он не обрывается на этажах без оповещателей.
                double riser = trunkLeft + trunkWidth * (index + 0.5) / trunks.Count;
                Draw.AddLine(document, view, created, riser, top, riser, bottom, style);

                double busY;
                if (!buses.TryGetValue(trunk.Name, out busY)) continue;

                // Шина расходится от стояка в обе стороны: стояк посередине, и половина приборов
                // остаётся слева от него.
                double from, to;
                if (!Reach(band, trunk, bodyLeft, trunkLeft, trunkWidth, scale, out from, out to)) continue;

                Draw.AddLine(document, view, created,
                    Math.Min(from, riser), busY, Math.Max(to, riser), busY, style);

                result.Trunks++;

                // Подпись шины — номера шлейфов этажа: «АЛС 1/2, 1/3». Ставится за правым краем
                // схемы: на самой схеме её некуда деть, а на выпущенном листе выходы линий
                // подписаны именно справа.
                string caption = Caption(band, trunk);
                if (caption.Length == 0) continue;

                Draw.AddText(document, view, textTypeId, created, caption,
                    right + padding * 2, busY, Draw.ToModel(40, scale), HorizontalTextAlignment.Left);
            }
        }

        /// <summary>Уровни шин внутри строки: снизу вверх, по одной на систему.</summary>
        private static Dictionary<string, double> Buses(
            IList<TrunkLine> trunks,
            double top,
            double height,
            double bandHeader,
            int scale)
        {
            var buses = new Dictionary<string, double>();
            if (trunks.Count == 0) return buses;

            double bottom = top - height;
            double area = height - bandHeader;

            // Шины идут под блоками, у нижнего края строки: вести их через блоки значит
            // перечеркнуть УГО.
            double gap = Math.Min(Draw.ToModel(3.0, scale), area / (trunks.Count + 1.0));

            for (int index = 0; index < trunks.Count; index++)
            {
                buses[trunks[index].Name] = bottom + gap * (index + 1);
            }

            return buses;
        }

        /// <summary>Шлейфы этажа для подписи шины. Их обычно один-два, больше трёх не пишем.</summary>
        private static string Caption(MatrixBand band, TrunkLine trunk)
        {
            List<string> loops = band.Cells
                .SelectMany(c => c.Blocks)
                .Where(b => TrunkLine.Of(b.Code) == trunk && b.Loop.Length > 0)
                .Select(b => b.Loop)
                .Distinct()
                .OrderBy(l => l, NaturalComparer.Instance)
                .ToList();

            if (loops.Count == 0) return string.Empty;
            if (loops.Count > 3) return trunk.Name + " " + string.Join(", ", loops.Take(3)) + "…";

            return trunk.Name + " " + string.Join(", ", loops);
        }

        /// <summary>
        /// Докуда тянуть шину: от левого края первой ячейки с приборами этой системы до правого
        /// края последней. Ячейки обходят колонку стояков, и обход надо повторить — иначе шина
        /// не дотянется до дальних ячеек ровно на её ширину.
        /// </summary>
        private static bool Reach(
            MatrixBand band,
            TrunkLine trunk,
            double bodyLeft,
            double trunkLeft,
            double trunkWidth,
            int scale,
            out double from,
            out double to)
        {
            double x = bodyLeft;
            bool found = false;

            from = 0.0;
            to = 0.0;

            for (int index = 0; index < band.Cells.Count; index++)
            {
                if (index == band.SplitIndex && trunkLeft > bodyLeft) x += trunkWidth;

                MatrixCell cell = band.Cells[index];
                double width = Draw.ToModel(cell.WidthMm, scale);

                if (cell.Blocks.Any(block => TrunkLine.Of(block.Code) == trunk))
                {
                    if (!found) from = x;
                    to = x + width;
                    found = true;
                }

                x += width;
            }

            return found;
        }

        /// <summary>
        /// Подпись этажа в левой колонке — вертикальная, как на листе: колонка узкая, а текст
        /// «6 этаж на отм. +15,600» поперёк в неё не влезет ни при какой ширине.
        /// </summary>
        private static void FloorLabel(
            Document document,
            View view,
            ElementId textTypeId,
            List<ElementId> created,
            MatrixBand band,
            double floorWidth,
            double top,
            double height,
            double padding)
        {
            double middle = top - height / 2.0;

            TextNote note = Draw.AddText(document, view, textTypeId, created, band.Caption,
                floorWidth / 2.0, middle, Math.Max(height - 2 * padding, floorWidth),
                HorizontalTextAlignment.Center);

            if (note == null) return;

            try
            {
                // Точка вставки у текста с выравниванием по центру и середине — его же центр,
                // поэтому поворот вокруг неё оставляет подпись в своей клетке.
                Line axis = Line.CreateBound(new XYZ(floorWidth / 2.0, middle, 0),
                    new XYZ(floorWidth / 2.0, middle, 1));

                ElementTransformUtils.RotateElement(document, note.Id, axis, Math.PI / 2.0);
            }
            catch (Exception)
            {
                // Не повернулось — подпись останется горизонтальной и вылезет за колонку,
                // но строка на месте, и это видно глазами.
            }
        }

        /// <summary>
        /// Ячейка помещения: черта под уже поставленным заголовком и ряды блоков ниже.
        /// Заголовок ставится раньше и всей строкой сразу — его высота определяет высоту полосы.
        /// </summary>
        private static void Cell(
            Document document,
            View view,
            ElementId textTypeId,
            List<ElementId> created,
            MatrixCell cell,
            Dictionary<int, ElementId> ugo,
            Dictionary<string, double> buses,
            Dictionary<string, GraphicsStyle> styles,
            MatrixResult result,
            double left,
            double top,
            double width,
            double headerHeight,
            double blockHeight,
            double padding)
        {
            Draw.AddLine(document, view, created, left, top - headerHeight, left + width, top - headerHeight);

            if (cell.IsEmpty) return;

            double slot = width / Math.Max(1, cell.PerLine);
            int placed = 0;

            for (int line = 0; line < cell.Lines && placed < cell.Blocks.Count; line++)
            {
                int count = Math.Min(cell.PerLine, cell.Blocks.Count - placed);

                // Неполная последняя строка центрируется в ячейке, а не жмётся влево.
                double start = left + (width - count * slot) / 2.0;
                double middle = top - headerHeight - (line + 0.5) * blockHeight;

                for (int index = 0; index < count; index++)
                {
                    MatrixBlock block = cell.Blocks[placed + index];
                    double centerX = start + (index + 0.5) * slot;

                    Block(document, view, textTypeId, created, block, ugo, result,
                        centerX, middle, Math.Max(slot - padding, padding), blockHeight);

                    Drop(document, view, created, block, buses, styles, centerX, middle, blockHeight);
                }

                placed += count;
            }
        }

        /// <summary>
        /// Блок устройства. Это один экземпляр аннотации УГО: код и количество у неё внутри,
        /// параметрами «Марка» и «N шт.» — так собраны выпущенные листы, и подписывать их
        /// отдельными текстами значило бы дублировать то, что семейство уже умеет.
        ///
        /// УГО не сопоставлено — блок рисуется текстом в две строки, и это видно глазами.
        /// </summary>
        private static void Block(
            Document document,
            View view,
            ElementId textTypeId,
            List<ElementId> created,
            MatrixBlock block,
            Dictionary<int, ElementId> ugo,
            MatrixResult result,
            double centerX,
            double centerY,
            double width,
            double height)
        {
            ElementId symbolId;
            FamilyInstance placed = null;

            if (ugo.TryGetValue(block.TypeId.IntegerValue, out symbolId))
            {
                placed = PlaceUgo(document, view, created, symbolId, centerX, centerY);
            }

            if (placed == null)
            {
                Draw.AddText(document, view, textTypeId, created, block.Caption + "\n" + block.Amount,
                    centerX, centerY, width, HorizontalTextAlignment.Center);

                result.WithoutUgo++;
                return;
            }

            // Половина УГО в библиотеке подписей внутри не имеет — там код и количество
            // дописываются текстом сверху и снизу, иначе блок выйдет немым.
            if (!Caption(placed, UgoFinder.MarkParameter, block.Caption))
            {
                Draw.AddText(document, view, textTypeId, created, block.Caption,
                    centerX, centerY + height * 0.36, width, HorizontalTextAlignment.Center);
            }

            if (!Caption(placed, UgoFinder.AmountParameter, block.Amount))
            {
                Draw.AddText(document, view, textTypeId, created, block.Amount,
                    centerX, centerY - height * 0.36, width, HorizontalTextAlignment.Center);
            }

            result.WithUgo++;
        }

        private static FamilyInstance PlaceUgo(
            Document document,
            View view,
            List<ElementId> created,
            ElementId symbolId,
            double centerX,
            double centerY)
        {
            try
            {
                FamilySymbol symbol = document.GetElement(symbolId) as FamilySymbol;
                if (symbol == null) return null;

                // Неактивированный типоразмер поставить нельзя, а аннотация могла лежать в проекте
                // ни разу не использованной.
                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                FamilyInstance instance = document.Create.NewFamilyInstance(
                    new XYZ(centerX, centerY, 0), symbol, view);

                created.Add(instance.Id);
                return instance;
            }
            catch (Exception)
            {
                // Категория не ставится в черновой вид — блок останется текстовым.
                return null;
            }
        }

        /// <summary>
        /// Спуск от блока к шине своей системы — так приборы подключены и на выпущенном листе:
        /// блоки висят на магистрали короткими отводами, а не соединены между собой цепочкой.
        /// Цепочку пришлось бы придумывать: порядок обхода приборов в шлейфе из модели не следует.
        /// </summary>
        private static void Drop(
            Document document,
            View view,
            List<ElementId> created,
            MatrixBlock block,
            Dictionary<string, double> buses,
            Dictionary<string, GraphicsStyle> styles,
            double centerX,
            double centerY,
            double blockHeight)
        {
            TrunkLine trunk = TrunkLine.Of(block.Code);
            if (trunk == null || !buses.TryGetValue(trunk.Name, out double busY)) return;

            double from = centerY - blockHeight * 0.35;
            if (busY >= from) return;

            styles.TryGetValue(trunk.Name, out GraphicsStyle style);
            Draw.AddLine(document, view, created, centerX, from, centerX, busY, style);
        }

        /// <summary>Пишет подпись в параметр аннотации. Нет такого параметра — вернёт false.</summary>
        private static bool Caption(FamilyInstance instance, string parameterName, string value)
        {
            Parameter parameter = instance.LookupParameter(parameterName);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
            {
                return false;
            }

            parameter.Set(value);
            return true;
        }

        /// <summary>Ширина схемы на листе — по ней в мастере видно, влезет ли она в лист.</summary>
        public static double WidthMm(MatrixLayout layout, SchemeSettings settings)
        {
            return settings.MatrixFloorColumnMm + layout.BodyWidthMm;
        }

        /// <summary>Высота схемы на листе.</summary>
        public static double HeightMm(MatrixLayout layout, SchemeSettings settings)
        {
            return layout.Bands.Sum(b => settings.MatrixHeaderMm + b.Lines * settings.MatrixBlockHeightMm);
        }

        public static string SizeCaption(MatrixLayout layout, SchemeSettings settings)
        {
            return WidthMm(layout, settings).ToString("#,##0", CultureInfo.CurrentCulture) + " × " +
                   HeightMm(layout, settings).ToString("#,##0", CultureInfo.CurrentCulture) + " мм";
        }
    }
}
