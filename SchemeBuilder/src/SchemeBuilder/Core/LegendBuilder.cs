using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Итог построения — что нарисовано и что пропущено.</summary>
    internal class BuildResult
    {
        public int Created { get; set; }

        /// <summary>Строк в блоке типов линий.</summary>
        public int Lines { get; set; }

        public List<string> Skipped { get; } = new List<string>();
    }

    /// <summary>
    /// Рисует таблицу УГО в виде-легенде.
    ///
    /// Сам символ рисует не плагин, а штатный «Компонент легенды»: он показывает графику
    /// семейства как есть, поэтому таблица не разъезжается с планами при смене УГО в семействе.
    /// Создать такой компонент с нуля Revit API не даёт — поэтому в легенде размножается
    /// найденный там образец, а у копий переназначается тип.
    /// </summary>
    internal static class LegendBuilder
    {
        private const string TitleText = "Условные графические обозначения";

        /// <summary>Компонент-образец и легенда, в которой он лежит.</summary>
        public class Template
        {
            public Template(Element element, View source)
            {
                Element = element;
                Source = source;
            }

            public Element Element { get; }

            public View Source { get; }
        }

        /// <summary>Ищет в легенде компонент, который можно взять за образец для копий.</summary>
        public static Element FindTemplateComponent(Document document, View legend)
        {
            return new FilteredElementCollector(document, legend.Id)
                .OfCategory(BuiltInCategory.OST_LegendComponents)
                .WhereElementIsNotElementType()
                .FirstElement();
        }

        /// <summary>
        /// Образец берётся из целевой легенды, а если там его нет — из любой другой в проекте.
        /// Копировать компоненты между легендами Revit позволяет, и это снимает требование
        /// «вставьте образец руками» для проектов, где хоть одна легенда с компонентом есть.
        /// </summary>
        public static Template FindTemplate(Document document, View legend)
        {
            Element own = FindTemplateComponent(document, legend);
            if (own != null) return new Template(own, legend);

            foreach (View other in FindLegends(document))
            {
                if (other.Id == legend.Id) continue;

                Element found = FindTemplateComponent(document, other);
                if (found != null) return new Template(found, other);
            }

            return null;
        }

        public static List<View> FindLegends(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.ViewType == ViewType.Legend)
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Вызывается внутри открытой транзакции. Сначала сносит то, что нарисовал прошлый запуск,
        /// потом строит заново — иначе после каждой пересборки таблица дублировалась бы.
        /// </summary>
        public static BuildResult Build(
            Document document,
            View legend,
            IList<DeviceRow> rows,
            SchemeSettings settings)
        {
            var result = new BuildResult();

            Draw.RemovePrevious(document, settings.GeneratedIds);

            Template template = FindTemplate(document, legend);
            ElementId textTypeId = Draw.TextTypeId(document, settings.TextTypeId);

            // Сопоставленное УГО важнее компонента легенды: компонент показывает модельную графику
            // прибора в натуральную величину, и в легенде масштаба 1:1 шкаф на 800 мм накрывает
            // собой полтаблицы. Аннотация УГО нарисована в размерах листа.
            Dictionary<int, ElementId> ugo = UgoFinder.Resolve(document, settings);

            int scale = Math.Max(1, legend.Scale);
            double minRowHeight = Draw.ToModel(settings.RowHeightMm, scale);
            double symbolWidth = Draw.ToModel(settings.SymbolWidthMm, scale);
            double codeWidth = Draw.ToModel(settings.CodeWidthMm, scale);
            double descriptionWidth = Draw.ToModel(settings.DescriptionWidthMm, scale);
            double padding = Draw.ToModel(1.5, scale);
            double columnGap = Draw.ToModel(8.0, scale);
            double columnWidth = symbolWidth + codeWidth + descriptionWidth;

            var created = new List<ElementId>();
            List<DeviceRow> included = rows.Where(r => r.Include).ToList();

            if (settings.LegendMergeRows) included = Merge(included, ugo);

            double startY = -PlaceTitle(document, legend, textTypeId, created, columnWidth, padding, scale);

            // У каждой колонки свой нижний край: строки получаются разной высоты, и общий шаг
            // строки колонки бы развалил.
            var columnBottom = new Dictionary<int, double>();
            int perColumn = Math.Max(1, settings.RowsPerColumn);

            for (int index = 0; index < included.Count; index++)
            {
                DeviceRow row = included[index];

                int column = index / perColumn;
                if (!columnBottom.ContainsKey(column)) columnBottom[column] = startY;

                double left = column * (columnWidth + columnGap);
                double top = columnBottom[column];

                // Высота строки известна только после того, как Revit разложит описание по
                // строкам: длинные наименования занимают три-четыре строки, и фиксированный шаг
                // накладывал бы их друг на друга.
                double textLeft = left + symbolWidth + codeWidth + padding;
                TextNote description = Draw.AddText(document, legend, textTypeId, created, row.Description,
                    textLeft, top - minRowHeight / 2.0, descriptionWidth - 2 * padding,
                    HorizontalTextAlignment.Left);

                double rowHeight = minRowHeight;
                if (description != null)
                {
                    document.Regenerate();
                    double textHeight = Draw.HeightOf(description, legend);
                    rowHeight = Math.Max(minRowHeight, textHeight + 2 * padding);
                }

                double middle = top - rowHeight / 2.0;
                if (description != null) description.Coord = new XYZ(textLeft, middle, 0);

                DrawFrame(document, legend, created, left, top, rowHeight, symbolWidth, codeWidth, descriptionWidth);

                Draw.AddText(document, legend, textTypeId, created, row.Code,
                    left + symbolWidth + codeWidth / 2.0, middle, codeWidth * 0.9,
                    HorizontalTextAlignment.Center);

                PlaceSymbol(document, legend, template, row, ugo, created,
                    left + symbolWidth / 2.0, middle, result);

                columnBottom[column] = top - rowHeight;
                result.Created++;
            }

            if (settings.BuildLineLegend)
            {
                // Блок линий идёт под самой длинной колонкой таблицы: он не оборудование модели,
                // и вписывать его строкой между приборами неправильно.
                double bottom = columnBottom.Count == 0 ? startY : columnBottom.Values.Min();

                result.Lines = LineLegend(document, legend, textTypeId, created,
                    bottom - minRowHeight, minRowHeight, symbolWidth, codeWidth, descriptionWidth, padding);
            }

            settings.GeneratedIds.Clear();
            settings.GeneratedIds.AddRange(created.Select(id => id.IntegerValue));

            return result;
        }

        /// <summary>
        /// Блок типов линий: АЛС, ЛСО, ЛРО, питание. Это не оборудование модели — его нельзя
        /// собрать сканированием, поэтому состав задан списком, а начерченный отрезок берёт
        /// стиль линий проекта. Стиля нет в проекте — строка пропускается: рисовать сплошную
        /// вместо штриховой хуже, чем не рисовать вовсе.
        /// </summary>
        private static int LineLegend(
            Document document,
            View legend,
            ElementId textTypeId,
            List<ElementId> created,
            double top,
            double rowHeight,
            double symbolWidth,
            double codeWidth,
            double descriptionWidth,
            double padding)
        {
            int drawn = 0;
            double y = top;

            foreach (TrunkLine line in TrunkLine.Known)
            {
                GraphicsStyle style = Draw.LineStyle(document, line.StyleNames);
                if (style == null) continue;

                double middle = y - rowHeight / 2.0;

                DrawFrame(document, legend, created, 0, y, rowHeight, symbolWidth, codeWidth, descriptionWidth);

                // Отрезок во всю ширину колонки: штриховую от сплошной иначе не отличить.
                Draw.AddLine(document, legend, created,
                    padding, middle, symbolWidth - padding, middle, style);

                Draw.AddText(document, legend, textTypeId, created, line.Name,
                    symbolWidth + codeWidth / 2.0, middle, codeWidth * 0.9, HorizontalTextAlignment.Center);

                Draw.AddText(document, legend, textTypeId, created, line.Caption,
                    symbolWidth + codeWidth + padding, middle, descriptionWidth - 2 * padding,
                    HorizontalTextAlignment.Left);

                y -= rowHeight;
                drawn++;
            }

            return drawn;
        }

        /// <summary>
        /// Схлопывает строки, которые в таблице выглядят одинаково: один код, одно наименование,
        /// одно УГО. В модели у прибора бывает несколько типоразмеров — «ИП 212-64 с б/о W1.02»
        /// и «W1.03», — но в легенде это одна строка: различать их там нечем и незачем.
        ///
        /// Типоразмер с другим УГО (тот же извещатель, но с изолятором) остаётся отдельной
        /// строкой: у него другой символ, и объединять их значит соврать.
        /// </summary>
        private static List<DeviceRow> Merge(List<DeviceRow> rows, Dictionary<int, ElementId> ugo)
        {
            var merged = new List<DeviceRow>();
            var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

            foreach (DeviceRow row in rows)
            {
                ElementId symbol;
                string key = row.Code + "\t" + row.Description + "\t" +
                             (ugo.TryGetValue(row.TypeId.IntegerValue, out symbol)
                                 ? symbol.IntegerValue.ToString(CultureInfo.InvariantCulture)
                                 : "0");

                if (!seen.Add(key)) continue;

                merged.Add(row);
            }

            return merged;
        }

        /// <summary>Заголовок над таблицей. Возвращает, сколько места он занял по вертикали.</summary>
        private static double PlaceTitle(
            Document document,
            View legend,
            ElementId textTypeId,
            List<ElementId> created,
            double columnWidth,
            double padding,
            int scale)
        {
            TextNote title = Draw.AddText(document, legend, textTypeId, created, TitleText,
                columnWidth / 2.0, 0, columnWidth * 0.9, HorizontalTextAlignment.Center);

            if (title == null) return Draw.ToModel(6.0, scale);

            document.Regenerate();
            return Draw.HeightOf(title, legend) + padding * 2 + Draw.ToModel(2.0, scale);
        }

        private static void DrawFrame(
            Document document,
            View legend,
            List<ElementId> created,
            double left,
            double top,
            double rowHeight,
            double symbolWidth,
            double codeWidth,
            double descriptionWidth)
        {
            double right = left + symbolWidth + codeWidth + descriptionWidth;
            double bottom = top - rowHeight;

            Draw.AddLine(document, legend, created, left, top, right, top);
            Draw.AddLine(document, legend, created, left, bottom, right, bottom);
            Draw.AddLine(document, legend, created, left, top, left, bottom);
            Draw.AddLine(document, legend, created, right, top, right, bottom);
            Draw.AddLine(document, legend, created, left + symbolWidth, top, left + symbolWidth, bottom);
            Draw.AddLine(document, legend, created, left + symbolWidth + codeWidth, top, left + symbolWidth + codeWidth, bottom);
        }

        /// <summary>
        /// Копирует образец, переназначает у копии тип и ставит её в центр ячейки. Размер копии
        /// известен только после смены типа, поэтому позиционирование идёт последним шагом.
        /// </summary>
        private static void PlaceSymbol(
            Document document,
            View legend,
            Template template,
            DeviceRow row,
            Dictionary<int, ElementId> ugo,
            List<ElementId> created,
            double centerX,
            double centerY,
            BuildResult result)
        {
            if (PlaceUgo(document, legend, row, ugo, created, centerX, centerY)) return;

            if (template == null)
            {
                result.Skipped.Add(row + " — в проекте нет ни одного компонента легенды, символ не поставлен");
                return;
            }

            try
            {
                ICollection<ElementId> copies = ElementTransformUtils.CopyElements(
                    template.Source,
                    new List<ElementId> { template.Element.Id },
                    legend,
                    Transform.Identity,
                    new CopyPasteOptions());

                ElementId copyId = copies.FirstOrDefault();
                if (copyId == null || copyId == ElementId.InvalidElementId)
                {
                    result.Skipped.Add(row + " — не удалось скопировать компонент легенды");
                    return;
                }

                created.Add(copyId);
                Element copy = document.GetElement(copyId);

                Parameter component = copy.get_Parameter(BuiltInParameter.LEGEND_COMPONENT);
                if (component == null || component.IsReadOnly)
                {
                    result.Skipped.Add(row + " — у компонента легенды недоступен параметр типа");
                    return;
                }

                component.Set(row.TypeId);
                document.Regenerate();

                BoundingBoxXYZ box = copy.get_BoundingBox(legend);
                if (box == null) return;

                var center = new XYZ((box.Min.X + box.Max.X) / 2.0, (box.Min.Y + box.Max.Y) / 2.0, 0);
                ElementTransformUtils.MoveElement(document, copyId, new XYZ(centerX, centerY, 0) - center);
            }
            catch (Exception exception)
            {
                result.Skipped.Add(row + " — символ не поставлен: " + exception.Message);
            }
        }

        /// <summary>
        /// Ставит аннотацию УГО в колонку символа. Количество в легенде скрывается: «3 шт.» имеет
        /// смысл в ячейке схемы, а в таблице обозначений — нет.
        /// </summary>
        private static bool PlaceUgo(
            Document document,
            View legend,
            DeviceRow row,
            Dictionary<int, ElementId> ugo,
            List<ElementId> created,
            double centerX,
            double centerY)
        {
            ElementId symbolId;
            if (ugo == null || !ugo.TryGetValue(row.TypeId.IntegerValue, out symbolId)) return false;

            try
            {
                FamilySymbol symbol = document.GetElement(symbolId) as FamilySymbol;
                if (symbol == null) return false;

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    document.Regenerate();
                }

                FamilyInstance instance = document.Create.NewFamilyInstance(
                    new XYZ(centerX, centerY, 0), symbol, legend);

                created.Add(instance.Id);

                Parameter amount = instance.LookupParameter(UgoFinder.AmountVisibility);
                if (amount != null && !amount.IsReadOnly && amount.StorageType == StorageType.Integer)
                {
                    amount.Set(0);
                }

                return true;
            }
            catch (Exception)
            {
                // В легенду такую категорию поставить не дали — вернёмся к компоненту легенды.
                return false;
            }
        }
    }
}
