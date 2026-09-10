using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>
    /// Строка будущей легенды: один типоразмер оборудования.
    ///
    /// Код и описание собираются из модели, но остаются правимыми: в марках попадаются опечатки,
    /// а ADSK_Наименование хранит полный паспортный текст, который в таблицу не помещается.
    /// Правки живут в модели и переживают пересборку — см. <see cref="SettingsStorage"/>.
    /// </summary>
    internal class DeviceRow
    {
        public DeviceRow(ElementId typeId, string familyName, string typeName)
        {
            TypeId = typeId;
            FamilyName = familyName;
            TypeName = typeName;
        }

        public ElementId TypeId { get; }

        public string FamilyName { get; }

        public string TypeName { get; }

        /// <summary>Буквенный код УГО: ARK, BTH, UDP. Префикс марки или правка пользователя.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Текст описания в таблице.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Значения, как их даёт модель, до наложения правок. Нужны, чтобы отличить правку от
        /// совпадения: сохранять «правку», равную данным модели, — значит заморозить строку.
        /// </summary>
        public string ModelCode { get; set; } = string.Empty;

        public string ModelDescription { get; set; } = string.Empty;

        /// <summary>Сколько экземпляров этого типа стоит в модели. В таблицу не идёт, нужен для сверки.</summary>
        public int Count { get; set; }

        /// <summary>Попадёт ли строка в легенду.</summary>
        public bool Include { get; set; } = true;

        /// <summary>Код взят из правки, а не из марки.</summary>
        public bool CodeEdited { get; set; }

        /// <summary>Описание взято из правки, а не из параметров типа.</summary>
        public bool DescriptionEdited { get; set; }

        /// <summary>
        /// Код объявлен в типе параметром ADSK_Позиция, а не выведен из марок. Такому коду можно
        /// верить: марки ведут по шлейфу и адресу, и у дымового извещателя марка бывает
        /// «UDP1/2.253», хотя код у него BTH.
        /// </summary>
        public bool CodeFromParameter { get; set; }

        /// <summary>
        /// Разные экземпляры одного типа дали разные префиксы марки. Обычно это ошибка маркировки,
        /// и молча брать первый попавшийся нельзя — пользователь должен это увидеть.
        /// </summary>
        public List<string> ConflictingCodes { get; } = new List<string>();

        public bool HasCodeConflict => !CodeFromParameter && ConflictingCodes.Count > 1;

        /// <summary>Что не так со строкой. Пусто, когда всё в порядке.</summary>
        public string Problem
        {
            get
            {
                if (HasCodeConflict) return "разные коды в марках: " + string.Join(", ", ConflictingCodes);
                if (string.IsNullOrWhiteSpace(Code)) return "код не определён — нет ADSK_Позиция, марки без букв";
                if (string.IsNullOrWhiteSpace(Description)) return "нет описания";
                return string.Empty;
            }
        }

        public override string ToString() => FamilyName + ": " + TypeName;
    }
}
