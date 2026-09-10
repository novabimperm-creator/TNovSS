using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SchemeBuilder.Core
{
    /// <summary>Итог размещения на листе.</summary>
    internal class SheetResult
    {
        public ViewSheet Sheet { get; set; }

        /// <summary>Лист заведён этим запуском, а не найден готовым.</summary>
        public bool Created { get; set; }

        public string Problem { get; set; } = string.Empty;
    }

    /// <summary>
    /// Кладёт черновой вид со схемой на лист с основной надписью.
    ///
    /// Лист заводится один раз и переиспользуется: вид можно разместить только на одном листе,
    /// и повторное построение схемы не должно плодить листы-двойники.
    /// </summary>
    internal static class SheetBuilder
    {
        /// <summary>Вызывается внутри открытой транзакции.</summary>
        public static SheetResult Place(Document document, ViewDrafting view, SchemeSettings settings)
        {
            var result = new SheetResult();

            ViewSheet sheet = Existing(document, settings);
            FamilySymbol titleBlock = TitleBlock(document, settings.SheetTitleBlock);

            if (sheet == null)
            {
                try
                {
                    sheet = ViewSheet.Create(document, titleBlock?.Id ?? ElementId.InvalidElementId);
                    result.Created = true;
                }
                catch (Exception exception)
                {
                    result.Problem = "лист не создался: " + exception.Message;
                    return result;
                }

                Rename(sheet, settings);
                settings.SheetId = sheet.Id;
            }
            else
            {
                // Штамп на готовом листе меняется, если в настройках выбрали другой: иначе
                // однажды промахнувшись, лист пришлось бы удалять руками.
                Replace(document, sheet, titleBlock);
            }

            result.Sheet = sheet;

            // Вид уже лежит на этом листе — второй раз класть нельзя, Revit не позволит.
            if (Viewport.CanAddViewToSheet(document, sheet.Id, view.Id))
            {
                try
                {
                    Viewport.Create(document, sheet.Id, view.Id, Center(document, sheet));
                }
                catch (Exception exception)
                {
                    result.Problem = "вид не лёг на лист: " + exception.Message;
                }
            }

            return result;
        }

        /// <summary>Меняет штамп на листе, если он не тот. Совпал — не трогаем.</summary>
        private static void Replace(Document document, ViewSheet sheet, FamilySymbol titleBlock)
        {
            if (titleBlock == null) return;

            List<Element> placed = new FilteredElementCollector(document, sheet.Id)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsNotElementType()
                .ToList();

            if (placed.Any(e => e.GetTypeId() == titleBlock.Id)) return;

            try
            {
                if (placed.Count > 0) document.Delete(placed.Select(e => e.Id).ToList());

                if (!titleBlock.IsActive)
                {
                    titleBlock.Activate();
                    document.Regenerate();
                }

                document.Create.NewFamilyInstance(XYZ.Zero, titleBlock, sheet);
            }
            catch (Exception)
            {
                // Штамп не заменился — лист остаётся с прежним, схема на нём всё равно лежит.
            }
        }

        private static ViewSheet Existing(Document document, SchemeSettings settings)
        {
            if (settings.SheetId != ElementId.InvalidElementId &&
                document.GetElement(settings.SheetId) is ViewSheet saved && !saved.IsTemplate)
            {
                return saved;
            }

            string number = (settings.SheetNumber ?? string.Empty).Trim();
            if (number.Length == 0) return null;

            ViewSheet found = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(s => !s.IsTemplate &&
                                     string.Equals(s.SheetNumber, number, StringComparison.CurrentCultureIgnoreCase));

            if (found != null) settings.SheetId = found.Id;
            return found;
        }

        private static void Rename(ViewSheet sheet, SchemeSettings settings)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(settings.SheetNumber)) sheet.SheetNumber = settings.SheetNumber.Trim();
            }
            catch (Exception)
            {
                // Номер занят другим листом — оставляем тот, что дал Revit.
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(settings.SheetName)) sheet.Name = settings.SheetName.Trim();
            }
            catch (Exception)
            {
                // Имя не приняли — не повод отказываться от листа.
            }
        }

        /// <summary>Основные надписи проекта в разумном порядке — их же показывает мастер.</summary>
        public static List<FamilySymbol> TitleBlocks(Document document)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderByDescending(Rank)
                .ThenBy(b => b.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Основная надпись: заданная в настройках по имени, иначе самая похожая на штамп.
        ///
        /// По ширине листа выбирать не вышло: у штампов этих проектов параметры ширины и высоты
        /// не заполнены вовсе, и «самая широкая» вырождается в первую попавшуюся — ею оказался
        /// служебный «Начальный вид», который рамкой не является.
        /// </summary>
        private static FamilySymbol TitleBlock(Document document, string name)
        {
            List<FamilySymbol> blocks = TitleBlocks(document);
            if (blocks.Count == 0) return null;

            if (!string.IsNullOrWhiteSpace(name))
            {
                FamilySymbol chosen = blocks.FirstOrDefault(b =>
                    Caption(b).IndexOf(name.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);

                if (chosen != null) return chosen;
            }

            return blocks.First();
        }

        public static string Caption(FamilySymbol block)
        {
            return block.FamilyName + " : " + block.Name;
        }

        /// <summary>
        /// Насколько семейство похоже на рабочий штамп: «Форма 3» — основной лист комплекта,
        /// «Штамп» — тоже рамка, «Начальный вид» и «Сведения о проекте» — служебные.
        /// </summary>
        private static int Rank(FamilySymbol block)
        {
            string caption = Caption(block).ToUpperInvariant();

            if (caption.Contains("НАЧАЛЬНЫЙ") || caption.Contains("СВЕДЕНИЯ") ||
                caption.Contains("РАЗРЕШЕНИЕ") || caption.Contains("ТИТУЛЬН"))
            {
                return -1;
            }

            int rank = 0;
            if (caption.Contains("ШТАМП")) rank += 2;
            if (caption.Contains("ФОРМА 3")) rank += 3;
            if (caption.Contains("РД")) rank += 1;

            return rank;
        }

        /// <summary>Середина листа: точнее вид всё равно ставить некуда, дальше двигают руками.</summary>
        private static XYZ Center(Document document, ViewSheet sheet)
        {
            BoundingBoxUV outline = sheet.Outline;
            if (outline == null) return XYZ.Zero;

            return new XYZ((outline.Min.U + outline.Max.U) / 2.0, (outline.Min.V + outline.Max.V) / 2.0, 0);
        }
    }
}
