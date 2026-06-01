using Autodesk.Revit.DB;

namespace TNovSS
{
    public class CachedElement
    {
        public Element Element { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public XYZ Center { get; set; }

        // ДОБАВЛЕНО: Преобразованные данные для элементов из связанных файлов
        public XYZ TransformedCenter { get; set; }
        public BoundingBoxXYZ TransformedBbox { get; set; }

        // ДОБАВЛЕНО: Флаг, чтобы знать, откуда элемент
        public bool IsFromLink { get; set; }
    }
}