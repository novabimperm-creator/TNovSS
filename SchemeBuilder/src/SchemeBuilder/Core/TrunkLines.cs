using System;
using System.Collections.Generic;
using System.Linq;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Магистраль схемы: стояк одной системы и его шины по этажам.
    ///
    /// Систему прибора определяет его код: извещатели и модули сидят в адресной линии связи,
    /// оповещатели — в линиях оповещения, источники питания — в питающей. Разбор идёт по коду,
    /// а не по марке: марка ведётся по шлейфу и адресу и о системе ничего не говорит.
    /// </summary>
    internal class TrunkLine
    {
        public TrunkLine(string name, string styleNames, string caption, params string[] codes)
        {
            Name = name;
            StyleNames = styleNames.Split('|');
            Caption = caption;
            Codes = new HashSet<string>(codes, StringComparer.CurrentCultureIgnoreCase);
        }

        /// <summary>Короткое имя системы — оно же подпись стояка.</summary>
        public string Name { get; }

        /// <summary>
        /// Имена стиля линий в порядке предпочтения. Их несколько, потому что в разных проектах
        /// одну и ту же линию заводят по-разному: речевое оповещение бывает и «ЛРО», и
        /// «RBZ_Кабель_СОУЭ», а специального «ЛРО» в проекте может не оказаться вовсе.
        /// </summary>
        public string[] StyleNames { get; }

        /// <summary>Расшифровка для легенды линий.</summary>
        public string Caption { get; }

        public HashSet<string> Codes { get; }

        /// <summary>
        /// Типы линий в порядке выпущенной легенды. Коды взяты оттуда же: BTH и BTM — извещатели,
        /// UDP — устройства пуска, AM и SC — метки и релейные модули, BIAD и BIAL — оповещатели,
        /// UG — источник питания.
        ///
        /// Часть типов существует только в легенде: межприборный интерфейс, групповое питание и
        /// линии контроля клапанов не привязаны к прибору с кодом, и стояка у них нет. Кодов
        /// у таких строк нет, поэтому в магистрали они не попадают, а в легенде остаются.
        /// </summary>
        public static readonly TrunkLine[] Known =
        {
            new TrunkLine("АЛС", "АЛС|RBZ_Кабель_ПС", "Адресная линия связи (АЛС)",
                "BTH", "BTM", "UDP", "AM", "АМ", "SC", "MDU", "МДУ", "IZ", "ARK", "BIU", "PD", "PO"),

            new TrunkLine("ЛСО", "ЛСО", "Линия светозвукового оповещения (ЛСО)",
                "BIAS", "BIAL", "BIA"),

            new TrunkLine("ЛРО", "ЛРО|RBZ_Кабель_СОУЭ", "Линия речевого оповещения (ЛРО)",
                "BIAD", "SPM", "SP", "SRN", "SRn"),

            new TrunkLine("R3", "R3|RBZ_Кабель_RS-485",
                "Специализированный кольцевой межприборный интерфейс связи R3-Link"),

            new TrunkLine("Пит220", "Пит220|RBZ_Кабель_Питание", "Линия питания 220В",
                "UG", "BR"),

            new TrunkLine("Пит12/24", "Пит12/24", "Линия питания 12В"),

            new TrunkLine("Груп.кабель", "Груп.кабель 220/380|Пит 380В", "Групповая линия питания 220/380В"),

            new TrunkLine("КВД/КПД/ОЗК", "КВД/ КПД/ ОЗК|КВД/КПД/ОЗК", "Линии контроля и управления КВД/КПД/ОЗК")
        };

        /// <summary>Система прибора по его коду. Код незнаком — прибор в магистраль не попадает.</summary>
        public static TrunkLine Of(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            string trimmed = code.Trim();
            return Known.FirstOrDefault(line => line.Codes.Contains(trimmed));
        }

        /// <summary>Системы, которые вообще встречаются в этой схеме — в порядке <see cref="Known"/>.</summary>
        public static List<TrunkLine> Used(MatrixLayout layout)
        {
            var used = new List<TrunkLine>();

            foreach (TrunkLine line in Known)
            {
                bool found = layout.Bands
                    .SelectMany(b => b.Cells)
                    .SelectMany(c => c.Blocks)
                    .Any(block => Of(block.Code) == line);

                if (found) used.Add(line);
            }

            return used;
        }
    }
}
