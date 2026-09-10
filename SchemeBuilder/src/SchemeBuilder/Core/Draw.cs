using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Мелочи черчения, общие для легенды и матрицы схемы: линия, текст, замер, пересчёт
    /// миллиметров листа в координаты вида.
    ///
    /// Всё, что создаётся, складывается в список созданного — по нему пересборка сносит ровно
    /// то, что нарисовал прошлый запуск, и не трогает добавленное руками.
    /// </summary>
    internal static class Draw
    {
        /// <summary>
        /// Миллиметры на листе — во внутренние единицы вида. На листе и в координатах вида размер
        /// различается ровно на масштаб, и забыть про это — значит получить таблицу в сто раз
        /// крупнее листа.
        /// </summary>
        public static double ToModel(double paperMillimeters, int viewScale)
        {
            return UnitUtils.ConvertToInternalUnits(paperMillimeters * Math.Max(1, viewScale), UnitTypeId.Millimeters);
        }

        public static void AddLine(
            Document document,
            View view,
            List<ElementId> created,
            double x1,
            double y1,
            double x2,
            double y2,
            GraphicsStyle style = null)
        {
            try
            {
                Line line = Line.CreateBound(new XYZ(x1, y1, 0), new XYZ(x2, y2, 0));
                DetailCurve curve = document.Create.NewDetailCurve(view, line);
                created.Add(curve.Id);

                if (style != null) curve.LineStyle = style;
            }
            catch (Exception)
            {
                // Вырожденная линия при нулевой ширине колонки — рамка просто будет неполной.
            }
        }

        /// <summary>
        /// Стиль линии проекта по имени: «АЛС», «ЛСО», «Пит220». Магистрали должны быть начерчены
        /// теми же стилями, что и на выпущенных листах, — иначе схема не ляжет в общий комплект.
        /// Стиля нет — вернём null, и линия будет обычной.
        /// </summary>
        public static GraphicsStyle LineStyle(Document document, params string[] names)
        {
            if (names == null || names.Length == 0) return null;

            Category lines = Category.GetCategory(document, BuiltInCategory.OST_Lines);
            if (lines == null) return null;

            List<Category> styles = lines.SubCategories.Cast<Category>().ToList();

            // Имена перебираются по порядку: первое найденное и есть нужное, остальные — запасные
            // на случай другого проекта.
            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;

                Category found = styles.FirstOrDefault(s =>
                    string.Equals(s.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

                if (found != null) return found.GetGraphicsStyle(GraphicsStyleType.Projection);
            }

            return null;
        }

        public static TextNote AddText(
            Document document,
            View view,
            ElementId textTypeId,
            List<ElementId> created,
            string text,
            double x,
            double y,
            double width,
            HorizontalTextAlignment alignment)
        {
            if (string.IsNullOrWhiteSpace(text) || textTypeId == ElementId.InvalidElementId) return null;

            try
            {
                var options = new TextNoteOptions(textTypeId)
                {
                    HorizontalAlignment = alignment,
                    VerticalAlignment = VerticalTextAlignment.Middle
                };

                // Точка вставки — в координатах вида, а ширина поля — в размерах листа: Revit сам
                // растянет её на масштаб. Передать сюда координатную ширину значит получить текст
                // вдесятеро шире клетки — в легенде 1:1 это незаметно, а на схеме 1:10 сразу видно.
                double paperWidth = width / Math.Max(1, view.Scale);

                TextNote note = TextNote.Create(document, view.Id, new XYZ(x, y, 0), paperWidth, text, options);
                created.Add(note.Id);
                return note;
            }
            catch (Exception)
            {
                // Ширина меньше минимальной для типа текста — подписи не будет; это видно глазами
                // и чинится шириной колонки в настройках.
                return null;
            }
        }

        public static double HeightOf(Element element, View view)
        {
            BoundingBoxXYZ box = element?.get_BoundingBox(view);
            return box == null ? 0.0 : box.Max.Y - box.Min.Y;
        }

        /// <summary>Сносит то, что нарисовал прошлый запуск. Удалённое вручную просто пропускается.</summary>
        public static void RemovePrevious(Document document, List<int> generatedIds)
        {
            List<ElementId> alive = generatedIds
                .Select(id => new ElementId(id))
                .Where(id => document.GetElement(id) != null)
                .ToList();

            if (alive.Count > 0) document.Delete(alive);
            generatedIds.Clear();
        }

        /// <summary>
        /// Тип текста для построения. Выбранный пользователем важнее умолчания: в проекте
        /// десятки типов текста, и высота строки таблицы зависит от того, каким она набрана.
        /// </summary>
        public static ElementId TextTypeId(Document document, ElementId preferred)
        {
            if (preferred != ElementId.InvalidElementId && document.GetElement(preferred) is TextNoteType)
            {
                return preferred;
            }

            ElementId defaultType = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (defaultType != null && defaultType != ElementId.InvalidElementId) return defaultType;

            TextNoteType any = new FilteredElementCollector(document)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .FirstOrDefault();

            return any?.Id ?? ElementId.InvalidElementId;
        }
    }
}
