using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace TNovSS
{
    public class SpatialIndex
    {
        private const double CELL_SIZE = 2.0; // Размер ячейки сетки (2 метра)
        private Dictionary<(int, int, int), List<CachedElement>> _grid = new Dictionary<(int, int, int), List<CachedElement>>();

        public void AddElement(CachedElement element)
        {
            // Определяем ячейки, которые пересекает BoundingBox
            var bbox = element.IsFromLink ? element.TransformedBbox : element.BoundingBox;
            if (bbox == null) return;

            var minCell = GetCell(bbox.Min);
            var maxCell = GetCell(bbox.Max);

            for (int x = minCell.Item1; x <= maxCell.Item1; x++)
            {
                for (int y = minCell.Item2; y <= maxCell.Item2; y++)
                {
                    for (int z = minCell.Item3; z <= maxCell.Item3; z++)
                    {
                        var key = (x, y, z);
                        if (!_grid.ContainsKey(key))
                            _grid[key] = new List<CachedElement>();

                        if (!_grid[key].Contains(element))
                            _grid[key].Add(element);
                    }
                }
            }
        }

        public List<CachedElement> FindCandidates(BoundingBoxXYZ searchBox)
        {
            var candidates = new HashSet<CachedElement>();
            var minCell = GetCell(searchBox.Min);
            var maxCell = GetCell(searchBox.Max);

            for (int x = minCell.Item1; x <= maxCell.Item1; x++)
            {
                for (int y = minCell.Item2; y <= maxCell.Item2; y++)
                {
                    for (int z = minCell.Item3; z <= maxCell.Item3; z++)
                    {
                        if (_grid.TryGetValue((x, y, z), out var cellElements))
                        {
                            foreach (var elem in cellElements)
                            {
                                // Быстрая проверка BoundingBox перед добавлением
                                var elemBbox = elem.IsFromLink ? elem.TransformedBbox : elem.BoundingBox;
                                if (DoBoundingBoxIntersect(searchBox, elemBbox))
                                {
                                    candidates.Add(elem);
                                }
                            }
                        }
                    }
                }
            }

            return new List<CachedElement>(candidates);
        }

        private (int, int, int) GetCell(XYZ point)
        {
            return (
                (int)Math.Floor(point.X / CELL_SIZE),
                (int)Math.Floor(point.Y / CELL_SIZE),
                (int)Math.Floor(point.Z / CELL_SIZE)
            );
        }

        private bool DoBoundingBoxIntersect(BoundingBoxXYZ bb1, BoundingBoxXYZ bb2)
        {
            return (bb1.Min.X <= bb2.Max.X && bb1.Max.X >= bb2.Min.X) &&
                   (bb1.Min.Y <= bb2.Max.Y && bb1.Max.Y >= bb2.Min.Y) &&
                   (bb1.Min.Z <= bb2.Max.Z && bb1.Max.Z >= bb2.Min.Z);
        }

        // ДОБАВЛЕНО: Очистка индекса
        public void Clear()
        {
            _grid.Clear();
        }
    }
}