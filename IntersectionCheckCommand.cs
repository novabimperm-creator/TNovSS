using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using TNovCommon;

namespace TNovSS
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class IntersectionCheckCommand : IExternalCommand
    {
        private Stopwatch _stopwatch;
        private ProgressForm _progressForm;
        private bool _cancelled = false;
        private int _processedCount = 0;
        private int _totalElements = 0;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string TNovClassName = "Проверка пересечений"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;

            //проверка подключения, запись в журнал
            //if(ServerUtils.CheckConnection(TNovClassName, TNovVersion)==false) return Result.Failed;

            // создание log - файла
            Logger.Initialize(TNovClassName,dateTime,TNovVersion);

            try
            {
                
                _stopwatch = Stopwatch.StartNew();

                Logger.Log("1. Выбор связанного файла", 1);
                // 1. ВЫБОР СВЯЗАННОГО ФАЙЛА
                LinkSelectionForm linkForm = new LinkSelectionForm(doc);
                if (linkForm.ShowDialog() != DialogResult.OK || linkForm.SelectedLink == null)
                {
                    Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                }

                RevitLinkInstance selectedLink = linkForm.SelectedLink;
                Document linkDoc = selectedLink.GetLinkDocument();
                Transform linkTransform = selectedLink.GetTotalTransform();

                Logger.Log("2. Выбор категорий", 1);
                // 2. ВЫБОР КАТЕГОРИЙ ДЛЯ ПРОВЕРКИ
                CategorySelectionForm categoryForm = new CategorySelectionForm(doc);
                if (categoryForm.ShowDialog() != DialogResult.OK)
                {
                    Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                }

                var selectedCategories = categoryForm.GetSelectedCategories();
                if (selectedCategories.Count == 0)
                {
                    new InfoWindow280("Не выбрано ни одной категории для проверки.").ShowDialog();
                    Logger.Log("Не выбрано ни одной категории для проверки. Завершение работы.", 3); return Result.Cancelled;
                }

                Logger.Log("3. Запуск проверки", 1);
                // 3. ЗАПУСК ПРОВЕРКИ С ПРОГРЕССОМ
                _progressForm = new ProgressForm("Проверка пересечений",
                    $"Проверка пересечений с файлом: {linkDoc.Title}");
                _progressForm.Show();
                _progressForm.SetCancelHandler(() => _cancelled = true);

                // ВЫПОЛНЯЕМ ПРОВЕРКУ
                List<IntersectionResult> intersections = PerformIntersectionCheck(
                    doc, linkDoc, selectedLink, selectedCategories, linkTransform);

                _progressForm.Close();
                _progressForm.Dispose();
                _stopwatch.Stop();

                Logger.Log("4. Показ результатов", 1);
                // 4. ПОКАЗ РЕЗУЛЬТАТОВ
                if (intersections.Count > 0 && !_cancelled)
                {
                    string stats = $"Найдено пересечений: {intersections.Count}\n" +
                                 $"Файл: {linkDoc.Title}\n" +
                                 $"Время: {_stopwatch.Elapsed.TotalSeconds:F1} сек\n" +
                                 $"Проверено элементов: {_processedCount}";

                    IntersectionResultsForm form = new IntersectionResultsForm(uiApp, intersections, stats);
                    form.ShowDialog();
                }
                else if (!_cancelled)
                {
                    new InfoWindow280(
                        $"Пересечений не найдено с файлом: {linkDoc.Title}\n" +
                        $"Время выполнения: {_stopwatch.Elapsed.TotalSeconds:F1} сек\n" +
                        $"Проверено элементов: {_processedCount}").ShowDialog();
                }
                Logger.Log("Завершение работы.", 5);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                _progressForm?.Close();
                _progressForm?.Dispose();
                message = $"Ошибка при проверке пересечений: {ex.Message}";
                Logger.Log(message, 4);
                return Result.Failed;
            }
        }

        private List<IntersectionResult> PerformIntersectionCheck(
            Document currentDoc,
            Document linkedDoc,
            RevitLinkInstance linkInstance,
            List<BuiltInCategory> categories,
            Transform transform)
        {
            var intersections = new List<IntersectionResult>();
            _processedCount = 0;

            // ОПТИМИЗАЦИЯ: Кэшируем элементы с BoundingBox
            _progressForm.UpdateProgress(10, "Кэширование элементов текущего файла...");
            var currentElements = CacheElementsWithBoundingBox(currentDoc, categories, false, transform);

            if (_cancelled || currentElements.Count == 0) return intersections;

            _progressForm.UpdateProgress(30, "Кэширование элементов связанного файла...");
            var linkedElements = CacheElementsWithBoundingBox(linkedDoc, categories, true, transform);

            if (_cancelled || linkedElements.Count == 0) return intersections;

            // ОПТИМИЗАЦИЯ: Пространственный индекс для быстрого поиска
            _progressForm.UpdateProgress(50, "Построение пространственного индекса...");
            var spatialIndex = new SpatialIndex();

            foreach (var cachedElement in linkedElements)
            {
                if (_cancelled) break;
                spatialIndex.AddElement(cachedElement);
            }

            _totalElements = currentElements.Count;

            // ПОИСК ПЕРЕСЕЧЕНИЙ
            _progressForm.UpdateProgress(60, "Поиск пересечений...");
            intersections = FindIntersections(currentElements, spatialIndex, linkedDoc.Title, linkInstance.Id);

            return intersections;
        }

        private List<CachedElement> CacheElementsWithBoundingBox(Document doc, List<BuiltInCategory> categories, bool isFromLink, Transform transform)
        {
            var elements = new List<CachedElement>();
            var filter = new ElementMulticategoryFilter(categories);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(filter);

            int total = collector.GetElementCount();
            int processed = 0;

            foreach (Element element in collector)
            {
                if (_cancelled) break;

                try
                {
                    var bbox = element.get_BoundingBox(null);
                    if (bbox != null)
                    {
                        var cachedElement = new CachedElement
                        {
                            Element = element,
                            BoundingBox = bbox,
                            Center = (bbox.Min + bbox.Max) * 0.5,
                            IsFromLink = isFromLink
                        };

                        // Для элементов из связанного файла преобразуем координаты
                        if (isFromLink && transform != null)
                        {
                            cachedElement.TransformedCenter = transform.OfPoint(cachedElement.Center);

                            var transformedBbox = new BoundingBoxXYZ
                            {
                                Min = transform.OfPoint(bbox.Min),
                                Max = transform.OfPoint(bbox.Max)
                            };
                            cachedElement.TransformedBbox = transformedBbox;
                        }
                        else
                        {
                            // Для текущего файла используем оригинальные данные
                            cachedElement.TransformedCenter = cachedElement.Center;
                            cachedElement.TransformedBbox = bbox;
                        }

                        elements.Add(cachedElement);
                    }
                }
                catch
                {
                    // Пропускаем элементы с проблемной геометрией
                }

                processed++;
                if (processed % 100 == 0)
                {
                    int progress = isFromLink
                        ? 30 + (int)((processed * 20.0) / total)
                        : 10 + (int)((processed * 20.0) / total);

                    string message = isFromLink
                        ? $"Кэширование связанных: {processed}/{total}"
                        : $"Кэширование текущих: {processed}/{total}";

                    _progressForm?.UpdateProgress(progress, message);
                }
            }

            return elements;
        }


        private List<IntersectionResult> FindIntersections(
            List<CachedElement> currentElements,
            SpatialIndex spatialIndex,
            string linkName,
            ElementId linkInstanceId)
        {
            var intersections = new List<IntersectionResult>();

            for (int i = 0; i < currentElements.Count; i++)
            {
                if (_cancelled) break;

                var currentElement = currentElements[i];
                _processedCount++;

                // Обновляем прогресс
                int progress = 60 + (int)((i * 40.0) / currentElements.Count);
                _progressForm?.UpdateProgress(progress, $"Проверка: {i + 1}/{currentElements.Count}");

                // Быстрый поиск кандидатов через пространственный индекс
                var candidates = spatialIndex.FindCandidates(currentElement.BoundingBox);

                foreach (var candidate in candidates)
                {
                    if (CheckElementsIntersectionOptimized(currentElement, candidate))
                    {
                        intersections.Add(new IntersectionResult
                        {
                            CurrentElementId = currentElement.Element.Id,
                            LinkedElementId = candidate.Element.Id,
                            LinkDocumentName = linkName,
                            LinkInstanceId = linkInstanceId,
                            CurrentElementName = currentElement.Element.Name,
                            LinkedElementName = candidate.Element.Name
                        });

                        // Ограничиваем для производительности
                        if (intersections.Count >= 1000)
                        {
                            _progressForm?.UpdateMessage("Достигнут лимит пересечений (1000)");
                            return intersections;
                        }
                    }
                }
            }

            return intersections;
        }
        private bool CheckElementsIntersectionOptimized(CachedElement elem1, CachedElement elem2)
        {
            try
            {
                // ВАЖНО: elem1 - из текущего файла, elem2 - из связанного (с преобразованными координатами)
                // Используем преобразованный центр для элемента из связанного файла
                XYZ center2 = elem2.IsFromLink ? elem2.TransformedCenter : elem2.Center;

                // Уровень 1: Проверка расстояния между центрами
                double distance = elem1.Center.DistanceTo(center2);
                double maxPossibleDistance = GetMaxDimension(elem1.BoundingBox) + GetMaxDimension(elem2.BoundingBox);

                if (distance > maxPossibleDistance * 1.5)
                    return false;

                // Уровень 2: Точная проверка BoundingBox
                // Используем преобразованный BoundingBox для элемента из связанного файла
                BoundingBoxXYZ bbox2 = elem2.IsFromLink ? elem2.TransformedBbox : elem2.BoundingBox;

                if (!DoBoundingBoxIntersect(elem1.BoundingBox, bbox2))
                    return false;

                // Уровень 3: Упрощенная геометрическая проверка
                return CheckSimplifiedGeometryIntersection(elem1.Element, elem2.Element);
            }
            catch
            {
                return false;
            }
        }

        private double GetMaxDimension(BoundingBoxXYZ bbox)
        {
            var size = bbox.Max - bbox.Min;
            return Math.Max(size.X, Math.Max(size.Y, size.Z));
        }

        private bool DoBoundingBoxIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            return (bb1.Min.X <= bb2.Max.X && bb1.Max.X >= bb2.Min.X) &&
                   (bb1.Min.Y <= bb2.Max.Y && bb1.Max.Y >= bb2.Min.Y) &&
                   (bb1.Min.Z <= bb2.Max.Z && bb1.Max.Z >= bb2.Min.Z);
        }

        private bool CheckSimplifiedGeometryIntersection(Element elem1, Element elem2)
        {
            try
            {
                Options options = new Options();
                options.DetailLevel = ViewDetailLevel.Medium; // Улучшено с Coarse на Medium для большей точности

                using (var geom1 = elem1.get_Geometry(options))
                using (var geom2 = elem2.get_Geometry(options))
                {
                    if (geom1 == null || geom2 == null)
                        return false;

                    foreach (GeometryObject obj1 in geom1)
                    {
                        Solid solid1 = obj1 as Solid;
                        if (solid1 == null || solid1.Faces.Size == 0 || solid1.Volume < 0.0001)
                            continue;

                        foreach (GeometryObject obj2 in geom2)
                        {
                            Solid solid2 = obj2 as Solid;
                            if (solid2 == null || solid2.Faces.Size == 0 || solid2.Volume < 0.0001)
                                continue;

                            try
                            {
                                var result = BooleanOperationsUtils.ExecuteBooleanOperation(
                                    solid1, solid2, BooleanOperationsType.Intersect);

                                if (result.Volume > 0.0001)
                                    return true;
                            }
                            catch
                            {
                                // Пропускаем проблемные пары
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}