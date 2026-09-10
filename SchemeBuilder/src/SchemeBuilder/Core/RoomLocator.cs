using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Ищет помещение под точкой оборудования.
    ///
    /// Помещения почти никогда не лежат в той же модели, что и слаботочка: их ведёт АР, а раздел
    /// подключает его связью. Поэтому поиск идёт и по текущему документу, и по всем загруженным
    /// RVT-связям, где помещения есть.
    /// </summary>
    internal class RoomLocator
    {
        private readonly List<Source> _sources = new List<Source>();

        public RoomLocator(Document document)
        {
            if (HasRooms(document)) _sources.Add(new Source(document, Transform.Identity));

            foreach (RevitLinkInstance link in new FilteredElementCollector(document)
                         .OfClass(typeof(RevitLinkInstance))
                         .Cast<RevitLinkInstance>())
            {
                Document linked = link.GetLinkDocument();
                if (linked == null || !HasRooms(linked)) continue;

                // Точка хоста переводится в координаты связи, а не наоборот: помещение ищется
                // в её собственном документе.
                _sources.Add(new Source(linked, link.GetTotalTransform().Inverse));
            }
        }

        /// <summary>Есть ли вообще где искать. Без источников зонирование по помещениям бессмысленно.</summary>
        public bool HasSources => _sources.Count > 0;

        /// <summary>Откуда берутся помещения — показывается в окне, чтобы связь-источник была видна.</summary>
        public IEnumerable<string> SourceNames => _sources.Select(s => s.Document.Title);

        /// <summary>
        /// Помещение под точкой. Если не нашлось — та же точка проверяется на нескольких высотах
        /// этажа: извещатель стоит под потолком и нередко оказывается выше верхней границы
        /// помещения, кнопка — на высоте пояса, а лючок в полу оказывается ниже нижней границы.
        /// По плану все они явно внутри, и терять их из-за границ объёма нельзя.
        /// </summary>
        public Room Find(XYZ point, double levelElevation)
        {
            Room room = FindAt(point);
            if (room != null) return room;

            double[] heights = { 1200.0, 100.0, 2000.0, 400.0 };

            foreach (double height in heights)
            {
                room = FindAt(new XYZ(point.X, point.Y, levelElevation + height / 304.8));
                if (room != null) return room;
            }

            return null;
        }

        private Room FindAt(XYZ point)
        {
            foreach (Source source in _sources)
            {
                try
                {
                    Room room = source.Document.GetRoomAtPoint(source.ToLocal.OfPoint(point));
                    if (room != null) return room;
                }
                catch (Exception)
                {
                    // Документ без фаз или без объёмов помещений — просто идём к следующему источнику.
                }
            }

            return null;
        }

        private static bool HasRooms(Document document)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Any();
        }

        private class Source
        {
            public Source(Document document, Transform toLocal)
            {
                Document = document;
                ToLocal = toLocal;
            }

            public Document Document { get; }

            public Transform ToLocal { get; }
        }
    }
}
