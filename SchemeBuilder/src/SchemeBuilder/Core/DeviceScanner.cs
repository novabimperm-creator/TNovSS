using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Сбор оборудования из модели: что реально стоит в проекте, то и попадает в легенду.
    /// Ничего не выдумывает — если типа нет в модели, строки не будет.
    /// </summary>
    internal static class DeviceScanner
    {
        /// <summary>Имя общего параметра ADSK, в котором лежит наименование оборудования.</summary>
        public const string DescriptionParameter = "ADSK_Наименование";

        /// <summary>
        /// Короткое наименование. Оно и нужно таблице: в <see cref="DescriptionParameter"/> лежит
        /// полный паспортный абзац на три строки, в колонку он не влезает.
        /// </summary>
        public const string ShortDescriptionParameter = "ADSK_Наименование краткое";

        /// <summary>
        /// Код УГО прибора — «BTH», «ИПР», «SC». Лежит в типе, и это единственный надёжный
        /// источник: марки в моделях ведут по шлейфам, и у дымового извещателя марка бывает
        /// «UDP1/2.253», хотя код у него BTH.
        /// </summary>
        public const string CodeParameter = "ADSK_Позиция";

        /// <summary>
        /// Ведущие буквы марки — это код УГО: «UDP1/2.148» даёт «UDP». Буквы берутся любого
        /// алфавита: часть марок ведут кириллицей.
        /// </summary>
        private static readonly Regex CodePrefix = new Regex(@"^\s*(\p{L}+)", RegexOptions.Compiled);

        /// <summary>
        /// Категории, в которых имеет смысл искать оборудование раздела. Показываются в окне
        /// с фактическим числом элементов — отмечает пользователь, а не плагин.
        /// </summary>
        public static readonly BuiltInCategory[] KnownCategories =
        {
            BuiltInCategory.OST_FireAlarmDevices,      // Пожарная сигнализация
            BuiltInCategory.OST_SecurityDevices,       // Устройства безопасности
            BuiltInCategory.OST_CommunicationDevices,  // Устройства связи
            BuiltInCategory.OST_DataDevices,           // Устройства передачи данных
            BuiltInCategory.OST_NurseCallDevices,      // Вызов персонала
            BuiltInCategory.OST_ElectricalEquipment,   // Электрооборудование: приборы, шкафы, ИБП
            BuiltInCategory.OST_ElectricalFixtures,    // Электроприборы
            BuiltInCategory.OST_GenericModel           // Обобщённые модели: чем добирают недостающее
        };

        /// <summary>Сколько экземпляров каждой категории есть в модели — для списка в окне.</summary>
        public static Dictionary<BuiltInCategory, int> CountByCategory(Document document)
        {
            var counts = new Dictionary<BuiltInCategory, int>();

            foreach (BuiltInCategory category in KnownCategories)
            {
                counts[category] = Instances(document, category).Count();
            }

            return counts;
        }

        /// <summary>
        /// Собирает типоразмеры выбранных категорий в строки легенды. Правки пользователя
        /// накладываются поверх данных модели.
        /// </summary>
        public static List<DeviceRow> Scan(
            Document document,
            IEnumerable<BuiltInCategory> categories,
            SchemeSettings settings)
        {
            var rows = new Dictionary<ElementId, DeviceRow>();
            var marks = new Dictionary<ElementId, List<string>>();

            foreach (BuiltInCategory category in categories)
            {
                foreach (Element instance in Instances(document, category))
                {
                    ElementId typeId = instance.GetTypeId();
                    if (typeId == ElementId.InvalidElementId) continue;

                    if (!rows.TryGetValue(typeId, out DeviceRow row))
                    {
                        if (!(document.GetElement(typeId) is ElementType type)) continue;

                        row = new DeviceRow(typeId, type.FamilyName, type.Name)
                        {
                            Description = ReadDescription(type)
                        };

                        rows[typeId] = row;
                        marks[typeId] = new List<string>();
                    }

                    row.Count++;

                    string code = CodeFromMark(instance);
                    if (!string.IsNullOrEmpty(code)) marks[typeId].Add(code);
                }
            }

            foreach (DeviceRow row in rows.Values)
            {
                // Код из типа важнее марок: марка ведётся по шлейфу и адресу, а не по прибору.
                ElementType type = document.GetElement(row.TypeId) as ElementType;
                string declared = type?.LookupParameter(CodeParameter)?.AsString();

                if (!string.IsNullOrWhiteSpace(declared))
                {
                    row.Code = declared.Trim();
                    row.CodeFromParameter = true;
                }
                else
                {
                    ApplyCode(row, marks[row.TypeId]);
                }

                row.ModelCode = row.Code;
                row.ModelDescription = row.Description;

                // Без кода в легенде делать нечего: так в таблицу лезут отверстия, гильзы и
                // закладные из обобщённых моделей. Пользователь может включить строку руками.
                row.Include = !string.IsNullOrWhiteSpace(row.Code);

                settings.ApplyTo(row);
            }

            // Порядок как в готовых легендах: по коду, а внутри кода по имени типа.
            return rows.Values
                .OrderBy(r => r.Code, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(r => r.TypeName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static IEnumerable<Element> Instances(Document document, BuiltInCategory category)
        {
            return new FilteredElementCollector(document)
                .OfCategory(category)
                .WhereElementIsNotElementType()
                .Where(e => e.GetTypeId() != ElementId.InvalidElementId);
        }

        /// <summary>
        /// Код берётся самый частый среди марок: одна опечатка не должна переименовать весь тип.
        /// Все встреченные варианты сохраняются, чтобы показать расхождение в окне.
        /// </summary>
        private static void ApplyCode(DeviceRow row, List<string> codes)
        {
            if (codes.Count == 0) return;

            List<IGrouping<string, string>> groups = codes
                .GroupBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ToList();

            row.Code = groups[0].Key;
            foreach (IGrouping<string, string> group in groups) row.ConflictingCodes.Add(group.Key);
        }

        private static string CodeFromMark(Element instance)
        {
            Parameter mark = instance.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            string value = mark?.AsString();
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            Match match = CodePrefix.Match(value);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        /// <summary>
        /// Наименование берётся сначала короткое, потом полное, потом встроенное «Описание».
        /// Порядок именно такой: полное — паспортный абзац на три строки («…предназначен для
        /// обнаружения загораний, сопровождающихся…»), и таблица от него разъезжается.
        /// </summary>
        private static string ReadDescription(ElementType type)
        {
            string value = type.LookupParameter(ShortDescriptionParameter)?.AsString();
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();

            value = type.LookupParameter(DescriptionParameter)?.AsString();
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();

            value = type.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION)?.AsString();
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
