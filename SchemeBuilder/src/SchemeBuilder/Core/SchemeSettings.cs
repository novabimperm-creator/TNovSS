using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Чем считается зона — колонка будущей матрицы «этаж × зона».</summary>
    internal enum ZoneSource
    {
        /// <summary>Имя помещения: «Межквартирный коридор», «Лифтовый холл».</summary>
        RoomName,

        /// <summary>Номер помещения.</summary>
        RoomNumber,

        /// <summary>Параметр помещения — им обычно и задан номер квартиры.</summary>
        RoomParameter,

        /// <summary>Параметр самого устройства: секция, зона, система.</summary>
        DeviceParameter,

        /// <summary>
        /// Квартира, а если помещение не квартирное — его имя. Это ровно колонки выпущенной
        /// схемы: «Квартира 1…9», «Межквартирный коридор», «Лифтовой холл». По одному имени
        /// помещения так не выйдет: внутри квартиры лежат «Жилая комната», «Кухня», «Коридор»,
        /// и они дробят колонку на части.
        /// </summary>
        ApartmentOrRoom
    }

    /// <summary>Чем упорядочены ячейки строки — колонки схемы слева направо.</summary>
    internal enum ZoneOrder
    {
        /// <summary>По координате X: дом вытянут вдоль оси X.</summary>
        AlongX,

        /// <summary>По координате Y: дом вытянут поперёк.</summary>
        AlongY,

        /// <summary>
        /// Сначала одна сторона коридора слева направо, потом другая. Стороны разделяются по
        /// средней Y этажа: в коридорном доме сортировка по одной координате их перемешивает —
        /// квартира с одной стороны встаёт между двумя с другой.
        /// </summary>
        SideThenX,

        /// <summary>Тем же по Y — для дома, вытянутого поперёк.</summary>
        SideThenY,

        /// <summary>По имени: «Квартира 1», «Квартира 2», … Порядок плана при этом теряется.</summary>
        ByName
    }

    /// <summary>Где на схеме идут стояки магистралей.</summary>
    internal enum TrunkPosition
    {
        /// <summary>Отдельной колонкой слева, сразу за подписью этажа.</summary>
        Left,

        /// <summary>
        /// Посередине схемы, в зоне ядра. Так они идут на выпущенном листе: стояки поднимаются
        /// у лестнично-лифтового узла, а не по фасаду.
        /// </summary>
        Middle,

        /// <summary>Справа, за последней ячейкой.</summary>
        Right
    }

    /// <summary>
    /// Настройки плагина и правки строк. Живут в модели, а не в профиле пользователя: работать
    /// с файлом будет тот, кто его открыл, и правки должны достаться ему, а не остаться на
    /// чужой машине.
    /// </summary>
    internal class SchemeSettings
    {
        private readonly Dictionary<int, RowOverride> _overrides = new Dictionary<int, RowOverride>();

        /// <summary>
        /// Выбранное УГО: типоразмер оборудования → «семейство аннотации и её типоразмер».
        /// По типу, а не по семейству: у «УДП Пуск дымоудаления» и «УДП Пуск пожаротушения»
        /// семейство одно, а УГО на схеме разные.
        /// </summary>
        private readonly Dictionary<int, string> _ugo = new Dictionary<int, string>();

        /// <summary>Категории, в которых искать оборудование.</summary>
        public HashSet<BuiltInCategory> Categories { get; } = new HashSet<BuiltInCategory>();

        /// <summary>Легенда, в которую строим. Хранится, чтобы не выбирать её каждый раз.</summary>
        public ElementId LegendViewId { get; set; } = ElementId.InvalidElementId;

        /// <summary>
        /// Что плагин нарисовал в прошлый раз. При пересборке удаляется именно это, а не всё
        /// подряд: в легенде может быть и то, что добавили руками.
        /// </summary>
        public List<int> GeneratedIds { get; } = new List<int>();

        public double RowHeightMm { get; set; } = 8.0;

        public double SymbolWidthMm { get; set; } = 14.0;

        public double CodeWidthMm { get; set; } = 16.0;

        public double DescriptionWidthMm { get; set; } = 95.0;

        /// <summary>Сколько строк в колонке, дальше таблица продолжается вправо.</summary>
        public int RowsPerColumn { get; set; } = 22;

        /// <summary>
        /// Чем считать зону. По умолчанию квартира: имя помещения дробит её на «Жилую комнату»,
        /// «Кухню» и «Гардероб», и схема раздувается втрое против выпущенной.
        /// </summary>
        public ZoneSource Zoning { get; set; } = ZoneSource.ApartmentOrRoom;

        /// <summary>Имя параметра для <see cref="ZoneSource.RoomParameter"/> и <see cref="ZoneSource.DeviceParameter"/>.</summary>
        public string ZoneParameterName { get; set; } = string.Empty;

        /// <summary>
        /// Параметр помещения с номером квартиры на этаже. Значение по умолчанию взято из
        /// модели АР: там номер квартиры в пределах этажа лежит именно в нём.
        /// </summary>
        public string ApartmentParameterName { get; set; } = "N_Кв.НомерНаЭтаже";

        /// <summary>Как подписывается квартира в колонке схемы.</summary>
        public string ApartmentPrefix { get; set; } = "Квартира ";

        /// <summary>Тип текста для таблицы. Пусто — берётся тип по умолчанию.</summary>
        public ElementId TextTypeId { get; set; } = ElementId.InvalidElementId;

        // ===== Матрица схемы =====

        /// <summary>Строить ли легенду УГО. Выключается, когда пересобирают одну схему.</summary>
        public bool BuildLegend { get; set; } = true;

        /// <summary>Чертить ли матрицу схемы.</summary>
        public bool BuildMatrix { get; set; } = true;

        /// <summary>
        /// Сводить ли подряд идущие этажи с одинаковым составом в одну строку «2–16 этаж (тип.)».
        /// </summary>
        public bool CollapseTypicalFloors { get; set; } = true;

        /// <summary>Черновой вид со схемой. Плагин заводит его сам — это API разрешает.</summary>
        public ElementId MatrixViewId { get; set; } = ElementId.InvalidElementId;

        /// <summary>Имя чернового вида, если его придётся создавать.</summary>
        public string MatrixViewName { get; set; } = "Структурная схема ПС (плагин)";

        /// <summary>Масштаб создаваемого вида. У существующего не трогается.</summary>
        public int MatrixScale { get; set; } = 10;

        /// <summary>Что нарисовала в схеме прошлая пересборка — сносится только это.</summary>
        public List<int> MatrixGeneratedIds { get; } = new List<int>();

        // Значения по умолчанию сняты с выпущенного листа «Структурная схема ПС вертикальная»:
        // блоки там идут с шагом около 12 мм, сами УГО 8–10 мм шириной и до 17 мм высотой вместе
        // с подписями, а строка этажа занимает 38 мм.

        /// <summary>Ширина блока устройства на листе: код, УГО, «3 шт.».</summary>
        public double MatrixBlockMm { get; set; } = 12.0;

        /// <summary>Высота блока устройства.</summary>
        public double MatrixBlockHeightMm { get; set; } = 30.0;

        /// <summary>Высота полосы с именем помещения в ячейке.</summary>
        public double MatrixHeaderMm { get; set; } = 6.0;

        /// <summary>Самая узкая ячейка: помещению с одним устройством всё равно нужно имя.</summary>
        public double MatrixCellMinMm { get; set; } = 16.0;

        /// <summary>Сколько блоков помещается в строку ячейки, дальше перенос на следующую.</summary>
        public int MatrixBlocksPerLine { get; set; } = 12;

        /// <summary>Ширина левой колонки с этажом и отметкой. Подпись в ней вертикальная.</summary>
        public double MatrixFloorColumnMm { get; set; } = 12.0;

        /// <summary>
        /// Чертить ли ячейку «зона не определена». По умолчанию нет: на листе ей не место,
        /// это брак данных. Сколько устройств так потерялось, плагин говорит после построения.
        /// </summary>
        public bool MatrixShowUnzoned { get; set; }

        /// <summary>
        /// Часть имени типа, по которой прибор считается этажным щитом. Такой прибор становится
        /// отдельной ячейкой «ЩТС 6.1», как на выпущенном листе: щит — узел схемы, а не ещё одно
        /// устройство в коридоре. Пусто — щиты не выделяются.
        /// </summary>
        public string MatrixPanelPattern { get; set; } = "Щит этажный";

        /// <summary>Как подписывается ячейка щита: к имени добавляется «этаж.номер».</summary>
        public string MatrixPanelPrefix { get; set; } = "ЩТС ";

        /// <summary>
        /// В каком радиусе от щита прибор считается стоящим в нём, мм. Ноль — не стягивать.
        ///
        /// Ноль по умолчанию не от осторожности: в модели 76-СУЗДАЛ модули стоят в 2–6 метрах от
        /// щита, у клапанов и в коридорах, а не в шкафу. Стянуть их по расстоянию значит соврать
        /// о составе щита. Где приборы правда смонтированы в шкафу, радиус ставится руками.
        /// </summary>
        public double MatrixPanelRadiusMm { get; set; }

        /// <summary>Где идут стояки магистралей.</summary>
        public TrunkPosition MatrixTrunkPosition { get; set; } = TrunkPosition.Middle;

        /// <summary>Чертить ли магистрали: стояки систем и шины по этажам.</summary>
        public bool MatrixDrawTrunks { get; set; } = true;

        /// <summary>Ширина колонки стояков между подписью этажа и ячейками.</summary>
        public double MatrixTrunkColumnMm { get; set; } = 14.0;

        /// <summary>
        /// Схлопывать ли строки легенды, одинаковые по коду, наименованию и УГО. В модели
        /// у прибора бывает несколько типоразмеров, а в таблице им место в одной строке.
        /// </summary>
        public bool LegendMergeRows { get; set; } = true;

        /// <summary>Добавлять ли под таблицу УГО блок с типами линий.</summary>
        public bool BuildLineLegend { get; set; } = true;

        // ===== Лист =====

        /// <summary>Класть ли схему на лист с основной надписью.</summary>
        public bool PlaceOnSheet { get; set; } = true;

        /// <summary>Лист со схемой. Заводится один раз: вид можно положить только на один лист.</summary>
        public ElementId SheetId { get; set; } = ElementId.InvalidElementId;

        public string SheetNumber { get; set; } = "СС-СС";

        public string SheetName { get; set; } = "Структурная схема ПС";

        /// <summary>Часть имени основной надписи. Пусто — берётся самая широкая из загруженных.</summary>
        public string SheetTitleBlock { get; set; } = string.Empty;

        /// <summary>
        /// Чем упорядочены ячейки в строке. На выпущенных листах порядок повторяет план, но по
        /// какой оси идёт дом — из модели не выведешь: у коридорного дома квартиры стоят по обе
        /// стороны, и сортировка по одной координате их перемешивает.
        /// </summary>
        public ZoneOrder MatrixOrder { get; set; } = ZoneOrder.AlongX;

        /// <summary>Развернуть порядок ячеек — когда схему читают с другого конца дома.</summary>
        public bool MatrixOrderReversed { get; set; }

        /// <summary>Ставить ли УГО в блоки схемы. Без сопоставления блок остаётся текстовым.</summary>
        public bool MatrixDrawUgo { get; set; } = true;

        /// <summary>Выбранное для типоразмера УГО: «семейство\tтипоразмер». Пусто — не выбрано.</summary>
        public string GetUgo(ElementId typeId)
        {
            return _ugo.TryGetValue(typeId.IntegerValue, out string saved) ? saved : string.Empty;
        }

        public void SetUgo(ElementId typeId, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                _ugo.Remove(typeId.IntegerValue);
                return;
            }

            _ugo[typeId.IntegerValue] = key;
        }

        public int UgoCount => _ugo.Count;

        /// <summary>
        /// Настройки для модели, где плагин ещё не работал.
        ///
        /// Категории отмечаются все, где в этих проектах лежит оборудование раздела, кроме
        /// обобщённых моделей: там отверстия и гильзы. Одной пожарной сигнализации мало —
        /// приборы, ИВЭПР, МДУ и оповещатели живут в электрооборудовании и устройствах связи,
        /// и схема без них выходит наполовину пустой, а понять это по одному экрану нельзя.
        /// </summary>
        public static SchemeSettings Default()
        {
            var settings = new SchemeSettings();

            settings.Categories.Add(BuiltInCategory.OST_FireAlarmDevices);
            settings.Categories.Add(BuiltInCategory.OST_CommunicationDevices);
            settings.Categories.Add(BuiltInCategory.OST_NurseCallDevices);
            settings.Categories.Add(BuiltInCategory.OST_SecurityDevices);
            settings.Categories.Add(BuiltInCategory.OST_ElectricalEquipment);

            return settings;
        }

        /// <summary>Накладывает сохранённые правки на строку, собранную из модели.</summary>
        public void ApplyTo(DeviceRow row)
        {
            if (!_overrides.TryGetValue(row.TypeId.IntegerValue, out RowOverride saved)) return;

            if (saved.Code != null)
            {
                row.Code = saved.Code;
                row.CodeEdited = true;
            }

            if (saved.Description != null)
            {
                row.Description = saved.Description;
                row.DescriptionEdited = true;
            }

            row.Include = saved.Include;
        }

        /// <summary>
        /// Запоминает правку. Значение, совпавшее с данными модели, не сохраняется — иначе
        /// строка навсегда застынет и перестанет реагировать на исправление параметров.
        /// </summary>
        public void Remember(DeviceRow row, string modelCode, string modelDescription)
        {
            var saved = new RowOverride
            {
                Code = string.Equals(row.Code, modelCode, StringComparison.Ordinal) ? null : row.Code,
                Description = string.Equals(row.Description, modelDescription, StringComparison.Ordinal)
                    ? null
                    : row.Description,
                Include = row.Include
            };

            if (saved.Code == null && saved.Description == null && saved.Include)
            {
                _overrides.Remove(row.TypeId.IntegerValue);
                return;
            }

            _overrides[row.TypeId.IntegerValue] = saved;
        }

        // ===== Хранение =====
        //
        // Формат намеренно текстовый и построчный: настройки должны читаться глазами при разборе
        // проблем и не тянуть за собой библиотеку сериализации в автономную сборку.

        public string Serialize()
        {
            var text = new StringBuilder();

            foreach (BuiltInCategory category in Categories)
            {
                text.AppendLine("CAT=" + ((int)category).ToString(CultureInfo.InvariantCulture));
            }

            text.AppendLine("VIEW=" + LegendViewId.IntegerValue.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("GEN=" + string.Join(",", GeneratedIds.Select(id => id.ToString(CultureInfo.InvariantCulture))));
            text.AppendLine("ROWH=" + RowHeightMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("SYMW=" + SymbolWidthMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("CODEW=" + CodeWidthMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("DESCW=" + DescriptionWidthMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("PERCOL=" + RowsPerColumn.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("ZONE=" + ((int)Zoning).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("ZONEPARAM=" + Escape(ZoneParameterName));
            text.AppendLine("FLATPARAM=" + Escape(ApartmentParameterName));
            text.AppendLine("FLATPREFIX=" + Escape(ApartmentPrefix));
            text.AppendLine("TEXTTYPE=" + TextTypeId.IntegerValue.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("LDRAW=" + (BuildLegend ? "1" : "0"));
            text.AppendLine("MDRAW=" + (BuildMatrix ? "1" : "0"));
            text.AppendLine("MTYPICAL=" + (CollapseTypicalFloors ? "1" : "0"));
            text.AppendLine("MVIEW=" + MatrixViewId.IntegerValue.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MNAME=" + Escape(MatrixViewName));
            text.AppendLine("MSCALE=" + MatrixScale.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MGEN=" + string.Join(",", MatrixGeneratedIds.Select(id => id.ToString(CultureInfo.InvariantCulture))));
            text.AppendLine("MBLOCKW=" + MatrixBlockMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MBLOCKH=" + MatrixBlockHeightMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MHEADH=" + MatrixHeaderMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MCELLMIN=" + MatrixCellMinMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MPERLINE=" + MatrixBlocksPerLine.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MFLOORW=" + MatrixFloorColumnMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MPANEL=" + Escape(MatrixPanelPattern));
            text.AppendLine("MPANELPRE=" + Escape(MatrixPanelPrefix));
            text.AppendLine("MPANELR=" + MatrixPanelRadiusMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MTRUNKPOS=" + ((int)MatrixTrunkPosition).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MTRUNK=" + (MatrixDrawTrunks ? "1" : "0"));
            text.AppendLine("MUNZONED=" + (MatrixShowUnzoned ? "1" : "0"));
            text.AppendLine("MTRUNKW=" + MatrixTrunkColumnMm.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("LLINES=" + (BuildLineLegend ? "1" : "0"));
            text.AppendLine("LMERGE=" + (LegendMergeRows ? "1" : "0"));
            text.AppendLine("SHEET=" + (PlaceOnSheet ? "1" : "0"));
            text.AppendLine("SHEETID=" + SheetId.IntegerValue.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("SHEETNUM=" + Escape(SheetNumber));
            text.AppendLine("SHEETNAME=" + Escape(SheetName));
            text.AppendLine("SHEETBLOCK=" + Escape(SheetTitleBlock));
            text.AppendLine("MORDER=" + ((int)MatrixOrder).ToString(CultureInfo.InvariantCulture));
            text.AppendLine("MREV=" + (MatrixOrderReversed ? "1" : "0"));
            text.AppendLine("MUGO=" + (MatrixDrawUgo ? "1" : "0"));

            foreach (KeyValuePair<int, string> pair in _ugo)
            {
                // Внутри значения уже есть табуляция «семейство\tтипоразмер» — экранирование её
                // сохраняет, поэтому разбирать строку можно по первой табуляции.
                text.AppendLine("UGO=" + pair.Key.ToString(CultureInfo.InvariantCulture) + "\t" + Escape(pair.Value));
            }

            foreach (KeyValuePair<int, RowOverride> pair in _overrides)
            {
                text.AppendLine("ROW=" + string.Join("\t", new[]
                {
                    pair.Key.ToString(CultureInfo.InvariantCulture),
                    Escape(pair.Value.Code),
                    Escape(pair.Value.Description),
                    pair.Value.Include ? "1" : "0"
                }));
            }

            return text.ToString();
        }

        public static SchemeSettings Deserialize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Default();

            var settings = new SchemeSettings();

            foreach (string line in text.Split('\n'))
            {
                // Только перевод строки: пробелы значимы. «Квартира » с пробелом на конце — это
                // префикс подписи, и обрезанный он даёт «Квартира6».
                string trimmed = line.Trim('\r', '\n');
                if (trimmed.Length == 0) continue;

                int separator = trimmed.IndexOf('=');
                if (separator <= 0) continue;

                string key = trimmed.Substring(0, separator);
                string value = trimmed.Substring(separator + 1);

                switch (key)
                {
                    case "CAT":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int category))
                        {
                            settings.Categories.Add((BuiltInCategory)category);
                        }

                        break;

                    case "VIEW":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int view))
                        {
                            settings.LegendViewId = new ElementId(view);
                        }

                        break;

                    case "GEN":
                        foreach (string part in value.Split(','))
                        {
                            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                            {
                                settings.GeneratedIds.Add(id);
                            }
                        }

                        break;

                    case "ROWH":
                        settings.RowHeightMm = ParseDouble(value, settings.RowHeightMm);
                        break;

                    case "SYMW":
                        settings.SymbolWidthMm = ParseDouble(value, settings.SymbolWidthMm);
                        break;

                    case "CODEW":
                        settings.CodeWidthMm = ParseDouble(value, settings.CodeWidthMm);
                        break;

                    case "DESCW":
                        settings.DescriptionWidthMm = ParseDouble(value, settings.DescriptionWidthMm);
                        break;

                    case "PERCOL":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int perColumn)
                            && perColumn > 0)
                        {
                            settings.RowsPerColumn = perColumn;
                        }

                        break;

                    case "ZONE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zoning)
                            && Enum.IsDefined(typeof(ZoneSource), zoning))
                        {
                            settings.Zoning = (ZoneSource)zoning;
                        }

                        break;

                    case "ZONEPARAM":
                        settings.ZoneParameterName = Unescape(value) ?? string.Empty;
                        break;

                    case "FLATPARAM":
                        settings.ApartmentParameterName = Unescape(value) ?? string.Empty;
                        break;

                    case "FLATPREFIX":
                        settings.ApartmentPrefix = Unescape(value) ?? string.Empty;
                        break;

                    case "TEXTTYPE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int textType))
                        {
                            settings.TextTypeId = new ElementId(textType);
                        }

                        break;

                    case "LDRAW":
                        settings.BuildLegend = value != "0";
                        break;

                    case "MDRAW":
                        settings.BuildMatrix = value != "0";
                        break;

                    case "MTYPICAL":
                        settings.CollapseTypicalFloors = value != "0";
                        break;

                    case "MVIEW":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int matrixView))
                        {
                            settings.MatrixViewId = new ElementId(matrixView);
                        }

                        break;

                    case "MNAME":
                        settings.MatrixViewName = Unescape(value) ?? string.Empty;
                        break;

                    case "MSCALE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int matrixScale)
                            && matrixScale > 0)
                        {
                            settings.MatrixScale = matrixScale;
                        }

                        break;

                    case "MGEN":
                        foreach (string part in value.Split(','))
                        {
                            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int matrixId))
                            {
                                settings.MatrixGeneratedIds.Add(matrixId);
                            }
                        }

                        break;

                    case "MBLOCKW":
                        settings.MatrixBlockMm = ParseDouble(value, settings.MatrixBlockMm);
                        break;

                    case "MBLOCKH":
                        settings.MatrixBlockHeightMm = ParseDouble(value, settings.MatrixBlockHeightMm);
                        break;

                    case "MHEADH":
                        settings.MatrixHeaderMm = ParseDouble(value, settings.MatrixHeaderMm);
                        break;

                    case "MCELLMIN":
                        settings.MatrixCellMinMm = ParseDouble(value, settings.MatrixCellMinMm);
                        break;

                    case "MPERLINE":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int perLine)
                            && perLine > 0)
                        {
                            settings.MatrixBlocksPerLine = perLine;
                        }

                        break;

                    case "MFLOORW":
                        settings.MatrixFloorColumnMm = ParseDouble(value, settings.MatrixFloorColumnMm);
                        break;

                    case "MUNZONED":
                        settings.MatrixShowUnzoned = value != "0";
                        break;

                    case "MPANEL":
                        settings.MatrixPanelPattern = Unescape(value) ?? string.Empty;
                        break;

                    case "MPANELPRE":
                        settings.MatrixPanelPrefix = Unescape(value) ?? string.Empty;
                        break;

                    case "MPANELR":
                        settings.MatrixPanelRadiusMm = ParseDouble(value, settings.MatrixPanelRadiusMm);
                        break;

                    case "MTRUNKPOS":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int trunkPos)
                            && Enum.IsDefined(typeof(TrunkPosition), trunkPos))
                        {
                            settings.MatrixTrunkPosition = (TrunkPosition)trunkPos;
                        }

                        break;

                    case "MTRUNK":
                        settings.MatrixDrawTrunks = value != "0";
                        break;

                    case "MTRUNKW":
                        settings.MatrixTrunkColumnMm = ParseDouble(value, settings.MatrixTrunkColumnMm);
                        break;

                    case "LMERGE":
                        settings.LegendMergeRows = value != "0";
                        break;

                    case "LLINES":
                        settings.BuildLineLegend = value != "0";
                        break;

                    case "SHEET":
                        settings.PlaceOnSheet = value != "0";
                        break;

                    case "SHEETID":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sheetId))
                        {
                            settings.SheetId = new ElementId(sheetId);
                        }

                        break;

                    case "SHEETNUM":
                        settings.SheetNumber = Unescape(value) ?? string.Empty;
                        break;

                    case "SHEETNAME":
                        settings.SheetName = Unescape(value) ?? string.Empty;
                        break;

                    case "SHEETBLOCK":
                        settings.SheetTitleBlock = Unescape(value) ?? string.Empty;
                        break;

                    case "MORDER":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order)
                            && Enum.IsDefined(typeof(ZoneOrder), order))
                        {
                            settings.MatrixOrder = (ZoneOrder)order;
                        }

                        break;

                    case "MREV":
                        settings.MatrixOrderReversed = value != "0";
                        break;

                    case "MUGO":
                        settings.MatrixDrawUgo = value != "0";
                        break;

                    case "UGO":
                        string[] ugo = value.Split(new[] { '\t' }, 2);
                        if (ugo.Length == 2 &&
                            int.TryParse(ugo[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int familyId))
                        {
                            settings._ugo[familyId] = Unescape(ugo[1]) ?? string.Empty;
                        }

                        break;

                    case "ROW":
                        ParseRow(settings, value);
                        break;
                }
            }

            if (settings.Categories.Count == 0) settings.Categories.Add(BuiltInCategory.OST_FireAlarmDevices);
            return settings;
        }

        private static void ParseRow(SchemeSettings settings, string value)
        {
            string[] parts = value.Split('\t');
            if (parts.Length < 4) return;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int typeId)) return;

            settings._overrides[typeId] = new RowOverride
            {
                Code = Unescape(parts[1]),
                Description = Unescape(parts[2]),
                Include = parts[3] != "0"
            };
        }

        private static double ParseDouble(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback;
        }

        // null отличается от пустой строки: null — «правки нет», пусто — «пользователь стёр текст».
        // Маркер безопасен: в экранированном тексте одиночный слеш перед нулём появиться не может.
        private const string NullMarker = "\\0";

        private static string Escape(string value)
        {
            if (value == null) return NullMarker;
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", string.Empty);
        }

        private static string Unescape(string value)
        {
            if (value == NullMarker) return null;
            return value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        private class RowOverride
        {
            public string Code;
            public string Description;
            public bool Include = true;
        }
    }
}
