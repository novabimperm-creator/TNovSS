using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Устройства одного типа в одной зоне — будущий блок ячейки «BTH, 3 шт.».</summary>
    internal class DeviceCount
    {
        private readonly Dictionary<string, int> _loops = new Dictionary<string, int>();

        public DeviceCount(ElementId typeId, string code, string typeName)
        {
            TypeId = typeId;
            Code = code;
            TypeName = typeName;
        }

        public ElementId TypeId { get; }

        public string Code { get; }

        public string TypeName { get; }

        public int Count { get; set; }

        /// <summary>Запоминает шлейф очередного прибора: «1/2» из марки «BTH1/2.9».</summary>
        public void Observe(string loop)
        {
            if (string.IsNullOrEmpty(loop)) return;

            _loops[loop] = _loops.TryGetValue(loop, out int found) ? found + 1 : 1;
        }

        /// <summary>
        /// Шлейф, в котором сидит большинство приборов этого типа в этой зоне. Разнобой бывает —
        /// квартира на границе шлейфов, — но линию рисуют по основному.
        /// </summary>
        public string Loop
        {
            get
            {
                string best = string.Empty;
                int most = 0;

                foreach (KeyValuePair<string, int> pair in _loops)
                {
                    if (pair.Value <= most) continue;

                    most = pair.Value;
                    best = pair.Key;
                }

                return best;
            }
        }
    }

    /// <summary>
    /// Зона этажа — ячейка схемы: квартира, межквартирный коридор, лифтовый холл, ЩТС.
    ///
    /// Зона это не имя, а помещение: в строке этажа «Межквартирный коридор» встречается дважды,
    /// слева и справа от ядра, и на схеме это две разные ячейки. Поэтому у зоны есть ключ,
    /// по которому она отличается от соседки, и отдельно имя, которое пишется на листе.
    /// </summary>
    internal class ZoneGroup
    {
        private double _sumX;
        private double _sumY;
        private int _points;

        public ZoneGroup(string key, string name)
        {
            Key = key;
            Name = name;
        }

        /// <summary>Чем зона отличается от других на этаже: квартира, помещение, значение параметра.</summary>
        public string Key { get; }

        /// <summary>Что пишется в заголовке ячейки.</summary>
        public string Name { get; private set; }

        /// <summary>
        /// Переименовывает зону. Нужно щитам: имя «ЩТС 6.1» складывается из номера этажа и порядка
        /// щитов на нём, а это известно только когда собран весь этаж.
        /// </summary>
        public void Rename(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) Name = name;
        }

        public List<DeviceCount> Devices { get; } = new List<DeviceCount>();

        public int Total
        {
            get
            {
                int total = 0;
                foreach (DeviceCount device in Devices) total += device.Count;
                return total;
            }
        }

        /// <summary>
        /// Средняя координата устройств зоны вдоль плана. По ней колонки схемы встают в том же
        /// порядке, в каком помещения идут по зданию, — на выпущенных листах порядок именно такой,
        /// а из имён его не вывести.
        /// </summary>
        public double AverageX => _points == 0 ? 0.0 : _sumX / _points;

        public double AverageY => _points == 0 ? 0.0 : _sumY / _points;

        public bool HasPosition => _points > 0;

        public void Observe(XYZ point)
        {
            if (point == null) return;

            _sumX += point.X;
            _sumY += point.Y;
            _points++;
        }
    }

    /// <summary>Этаж — строка будущей матрицы.</summary>
    internal class FloorGroup
    {
        public FloorGroup(ElementId levelId, string name, double elevation)
        {
            LevelId = levelId;
            Name = name;
            Elevation = elevation;
        }

        public ElementId LevelId { get; }

        public string Name { get; }

        /// <summary>Отметка уровня во внутренних единицах.</summary>
        public double Elevation { get; }

        public List<ZoneGroup> Zones { get; } = new List<ZoneGroup>();
    }

    /// <summary>
    /// Сводка по модели: что и где стоит. Это данные будущей схемы — сначала их надо увидеть
    /// цифрами и сверить с проектом, и только потом чертить.
    /// </summary>
    internal class SchemeData
    {
        public List<FloorGroup> Floors { get; } = new List<FloorGroup>();

        /// <summary>Всего разложенных по этажам и зонам устройств.</summary>
        public int Placed { get; set; }

        /// <summary>Устройства, для которых не нашлось помещения или значения параметра зоны.</summary>
        public int WithoutZone { get; set; }

        /// <summary>Устройства без уровня — в матрицу их положить некуда.</summary>
        public int WithoutLevel { get; set; }

        /// <summary>Откуда брались помещения и прочие пояснения к сводке.</summary>
        public List<string> Notes { get; } = new List<string>();
    }
}
