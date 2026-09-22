using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

using static TNovCommon.ElementIdCompat;

namespace SchemeBuilder.Core
{
    /// <summary>Аннотация проекта, которой можно нарисовать УГО прибора на схеме.</summary>
    internal class UgoCandidate
    {
        public UgoCandidate(FamilySymbol symbol)
        {
            Id = symbol.Id;
            FamilyName = symbol.FamilyName;
            SymbolName = symbol.Name;
            CategoryName = symbol.Category?.Name ?? string.Empty;
        }

        public ElementId Id { get; }

        public string FamilyName { get; }

        public string SymbolName { get; }

        public string CategoryName { get; }

        /// <summary>Как выбор хранится в модели. Имена, а не идентификатор: так его видно глазами.</summary>
        public string Key => FamilyName + "\t" + SymbolName;

        public override string ToString()
        {
            return string.Equals(SymbolName, FamilyName, StringComparison.CurrentCultureIgnoreCase)
                ? FamilyName
                : FamilyName + " : " + SymbolName;
        }
    }

    /// <summary>Строка сопоставления: один типоразмер оборудования и выбранное для него УГО.</summary>
    internal class UgoMatch
    {
        public UgoMatch(DeviceRow row)
        {
            Row = row;
        }

        public DeviceRow Row { get; }

        /// <summary>Что записано в параметре типа «Типоразмер УГО» — подсказка от библиотеки.</summary>
        public string Declared { get; set; } = string.Empty;

        public UgoCandidate Selected { get; set; }

        /// <summary>Выбор сделан плагином по подсказке, а не человеком и не прошлым запуском.</summary>
        public bool Guessed { get; set; }
    }

    /// <summary>
    /// Сопоставляет прибор с его УГО.
    ///
    /// УГО схемы — это готовые аннотационные семейства проекта (<c>RBZ_УГО-2D_Узел_*</c>): ими
    /// начерчены выпущенные листы, и внутри у них уже есть подписи «Марка» и «N шт.». Плоское УГО
    /// плана, вложенное в семейство прибора, для схемы не годится — оно другое.
    ///
    /// Соответствие подсказывает сама библиотека: у типа прибора есть параметр «Типоразмер УГО»
    /// со ссылкой на вложенное УГО, и его имя почти совпадает с именем аннотации схемы —
    /// «RBZ_УГО_ДИП(плоскость)» против «RBZ_УГО-2D_Узел_ДИП». Совпадение проверяется по
    /// очищенному имени, а спорное оставляется человеку.
    /// </summary>
    internal static class UgoFinder
    {
        /// <summary>Параметр типа со ссылкой на вложенное УГО — подсказка для сопоставления.</summary>
        public const string DeclaredParameter = "Типоразмер УГО";

        /// <summary>Подпись кода внутри аннотации УГО.</summary>
        public const string MarkParameter = "Марка";

        /// <summary>Подпись количества внутри аннотации УГО.</summary>
        public const string AmountParameter = "N шт.";

        public const string MarkVisibility = "Видимость марки";

        public const string AmountVisibility = "Видимость N шт.";

        private static readonly BuiltInCategory[] UgoCategories =
        {
            BuiltInCategory.OST_GenericAnnotation,   // Типовые аннотации
            BuiltInCategory.OST_DetailComponents     // Элементы узлов
        };

        /// <summary>Аннотации проекта, которыми можно рисовать УГО.</summary>
        public static List<UgoCandidate> Candidates(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(s => s.Category != null &&
                            UgoCategories.Contains((BuiltInCategory)s.Category.Id.IntValue()))
                .Select(s => new UgoCandidate(s))
                .OrderBy(c => c.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(c => c.SymbolName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Строит таблицу сопоставления: сохранённый выбор важнее подсказки, подсказка важнее пустоты.
        /// </summary>
        public static List<UgoMatch> Match(
            Document document,
            IEnumerable<DeviceRow> rows,
            IList<UgoCandidate> candidates,
            SchemeSettings settings)
        {
            var matches = new List<UgoMatch>();

            foreach (DeviceRow row in rows)
            {
                var match = new UgoMatch(row);
                ElementType type = document.GetElement(row.TypeId) as ElementType;

                match.Declared = type?.LookupParameter(DeclaredParameter)?.AsValueString() ?? string.Empty;

                string saved = settings.GetUgo(row.TypeId);
                if (!string.IsNullOrEmpty(saved))
                {
                    match.Selected = candidates.FirstOrDefault(c => c.Key == saved);
                }

                if (match.Selected == null)
                {
                    match.Selected = Guess(match, row, candidates);
                    match.Guessed = match.Selected != null;
                }

                matches.Add(match);
            }

            return matches;
        }

        /// <summary>
        /// Подбирает аннотацию по очищенному имени: «RBZ_УГО_ДИП(плоскость) : ДИП» → «ДИП» →
        /// «RBZ_УГО-2D_Узел_ДИП».
        /// </summary>
        private static UgoCandidate Guess(UgoMatch match, DeviceRow row, IList<UgoCandidate> candidates)
        {
            List<string> wanted = Wanted(match, row);
            if (wanted.Count == 0) return null;

            UgoCandidate best = null;
            int bestScore = 0;

            foreach (UgoCandidate candidate in candidates)
            {
                int score = ScoreOf(candidate, wanted);
                if (score <= bestScore) continue;

                bestScore = score;
                best = candidate;
            }

            // Случайное вхождение из двух букв — не подсказка, а шум.
            return bestScore >= 60000 ? best : null;
        }

        /// <summary>
        /// Имена, которые ищем среди аннотаций. Кроме очевидных — ссылки из «Типоразмер УГО»
        /// и имени типа — собираются составные: у «Рубежа» исполнение прибора записано словами
        /// в имени типа, а в имени УГО буквами («Пуск дымоудаления» → УДП_ДУ), и без этого
        /// «Пуск пожаротушения» с равным счётом попадает в первое попавшееся УДП.
        /// </summary>
        private static List<string> Wanted(UgoMatch match, DeviceRow row)
        {
            var wanted = new List<string>();
            string baseName = string.Empty;

            if (!string.IsNullOrWhiteSpace(match.Declared))
            {
                string[] parts = match.Declared.Split(':');
                for (int index = 0; index < parts.Length; index++)
                {
                    string part = Normalize(parts[index]);
                    wanted.Add(part);
                    if (index == 0) baseName = part;
                }

                wanted.Add(Normalize(match.Declared.Replace(":", " ")));
            }

            wanted.Add(Normalize(row.TypeName));

            string upper = (row.TypeName + " " + match.Declared).ToUpperInvariant();

            if (baseName.Length > 0)
            {
                string suffix = string.Empty;

                if (upper.Contains("ПОЖАРОТУШЕН")) suffix = "ПТ";
                else if (upper.Contains("ДЫМОУДАЛЕН")) suffix = "ДУ";
                else if (upper.Contains("АВАРИЙН")) suffix = "АВ";

                // Изолятор короткого замыкания и встроенный изолятор — отдельные УГО, и в имени
                // типа они помечены как «ИКЗ» и «ИЗ-1».
                bool shorted = upper.Contains("ИКЗ");
                bool isolator = upper.Contains("ИЗ-1") || upper.Contains("ИЗ1");

                if (suffix.Length > 0) wanted.Add(baseName + suffix);
                if (shorted) wanted.Add(baseName + "ИКЗ");
                if (isolator) wanted.Add(baseName + "ИЗ");
                if (suffix.Length > 0 && shorted) wanted.Add(baseName + suffix + "ИКЗ");
            }

            wanted.RemoveAll(string.IsNullOrEmpty);
            return wanted;
        }

        /// <summary>
        /// Насколько аннотация похожа на искомую.
        ///
        /// Счёт считается по сотням, а в единицах лежит длина совпавшего имени. Это нужно для
        /// ничьих: «ИП 212-64-R3/ИЗ-1Б» одинаково точно совпадает и с «ДИП», и с «ДИП_ИЗ»,
        /// а верное — длинное.
        /// </summary>
        private static int ScoreOf(UgoCandidate candidate, List<string> wanted)
        {
            string family = Normalize(candidate.FamilyName);
            string symbol = Normalize(candidate.SymbolName);

            int score = Best(wanted, family);
            int symbolScore = Best(wanted, symbol);

            if (score == 0)
            {
                // Совпал только типоразмер: у «Блока индикации» семейство названо словом, а тип —
                // как раз «БИУ». Такой подсказке веры меньше, чем совпадению по семейству.
                if (symbolScore / 1000 < 80) return 0;

                score = symbolScore - 20000;
            }
            else if (symbolScore / 1000 >= 80)
            {
                // Совпавший типоразмер поднимает семейство над однофамильцами: у «РМ-К» типы
                // «РМ-1К» и «РМ-4К», и выбирать между ними надо по имени прибора.
                score += 15000;
            }

            // Взрывозащищённое исполнение — редкий частный случай, и попадать в него случайно
            // нельзя: у обычного прибора в имени «Ex» нет.
            bool wantsEx = wanted.Any(want => want.Contains("EX") || want.Contains("ЕХ"));
            if (!wantsEx && (family.EndsWith("EX", StringComparison.Ordinal) ||
                             family.EndsWith("ЕХ", StringComparison.Ordinal)))
            {
                score -= 50000;
            }

            return score;
        }

        /// <summary>Лучшее совпадение имени с любым из искомых, с длиной совпавшего в младших разрядах.</summary>
        private static int Best(List<string> wanted, string name)
        {
            int best = 0;

            foreach (string want in wanted)
            {
                int similar = Similar(want, name);
                if (similar == 0) continue;

                // Длина только разводит ничьи и не должна превращать «не похоже» в «немного похоже».
                int score = similar * 1000 + Math.Min(want.Length, 999);
                if (score > best) best = score;
            }

            return best;
        }

        private static int Similar(string want, string name)
        {
            if (want.Length < 2 || name.Length < 2) return 0;

            if (name == want) return 100;
            if (want.StartsWith(name, StringComparison.Ordinal)) return 82;
            if (want.EndsWith(name, StringComparison.Ordinal)) return 80;
            if (name.StartsWith(want, StringComparison.Ordinal)) return 70;
            if (name.Contains(want) && want.Length >= 4) return 62;
            if (want.Contains(name) && name.Length >= 4) return 60;

            return 0;
        }

        /// <summary>
        /// Оставляет от имени только суть: «RBZ_УГО-2D_Узел_ДИП» и «RBZ_УГО_ДИП(плоскость)» дают
        /// одно и то же «ДИП». Регистр, пробелы, дефисы и скобки на совпадение влиять не должны.
        /// </summary>
        private static string Normalize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            string text = name.Trim();

            foreach (string prefix in new[] { "RBZ_УГО-2D_Узел_", "RBZ_УГО_", "УГО_", "RBZ_" })
            {
                if (text.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
                {
                    text = text.Substring(prefix.Length);
                    break;
                }
            }

            int bracket = text.IndexOf('(');
            if (bracket > 0) text = text.Substring(0, bracket);

            var clean = new StringBuilder();
            foreach (char symbol in text.ToUpperInvariant())
            {
                if (char.IsLetterOrDigit(symbol)) clean.Append(symbol);
            }

            return clean.ToString();
        }

        /// <summary>
        /// Сопоставление «тип прибора → аннотация», готовое к построению: имена превращаются
        /// в идентификаторы того проекта, в котором строим.
        /// </summary>
        public static Dictionary<int, ElementId> Resolve(Document document, SchemeSettings settings)
        {
            var map = new Dictionary<int, ElementId>();
            List<UgoCandidate> candidates = Candidates(document);

            foreach (FamilySymbol symbol in new FilteredElementCollector(document)
                         .OfClass(typeof(FamilySymbol))
                         .Cast<FamilySymbol>())
            {
                string saved = settings.GetUgo(symbol.Id);
                if (string.IsNullOrEmpty(saved)) continue;

                UgoCandidate found = candidates.FirstOrDefault(c => c.Key == saved);
                if (found != null) map[symbol.Id.IntValue()] = found.Id;
            }

            return map;
        }
    }
}
