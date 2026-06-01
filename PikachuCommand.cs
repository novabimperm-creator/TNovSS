using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using TNovCommon;

namespace TNovSS
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PikachuCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            string DBCommandName = "Расстановщик СС ПС"; DateTime dateTime = DateTime.Now; string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);

            // создание log - файла
            Logger.Initialize(DBCommandName, dateTime,TNovVersion);

            // Проверка на наличие открытого документа
            if (doc == null)
            {
                TaskDialog.Show("Ошибка", "Нет активного документа!");
                return Result.Failed;
            }


            try
            {
                // Основной цикл работы плагина
                bool shouldContinue = true;

                while (shouldContinue)
                {
                    Logger.Log("1. Выбор связанного файла", 1);
                    // 1. Выбор связанного файла
                    RevitLinkInstance selectedLink = null;
                    Document linkDoc = null;
                    bool linkSelected = false;

                    while (!linkSelected)
                    {
                        LinkSelectionForm linkForm = new LinkSelectionForm(doc);
                        var linkResult = linkForm.ShowDialog();

                        if (linkResult == DialogResult.OK && linkForm.SelectedLink != null)
                        {
                            selectedLink = linkForm.SelectedLink;
                            linkDoc = selectedLink.GetLinkDocument();

                            // Проверка на null связанного документа
                            if (linkDoc == null)
                            {
                                TaskDialog.Show("Ошибка", "Не удалось получить связанный документ!");
                                continue;
                            }

                            linkSelected = true; Logger.Log("Выбран связанный файл " + linkDoc.Title, 1);
                        }
                        else if (linkResult == DialogResult.Abort) // Назад
                        {
                            // Для первого шага - это отмена всего плагина
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3);
                            return Result.Cancelled;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3);
                            return Result.Cancelled;
                        }
                    }

                    Logger.Log("2. Выбор уровня в связанном файле", 1);
                    // 2. Выбор уровня в связанном файле
                    Level selectedLevel = null;
                    bool levelSelected = false;

                    while (!levelSelected)
                    {
                        LevelSelectionForm levelForm = new LevelSelectionForm(linkDoc);
                        var levelResult = levelForm.ShowDialog();

                        if (levelResult == DialogResult.OK && levelForm.SelectedLevel != null)
                        {
                            selectedLevel = levelForm.SelectedLevel;
                            levelSelected = true; Logger.Log("Выбран уровень " + selectedLevel.Name, 1);
                        }
                        else if (levelResult == DialogResult.Abort) // Назад
                        {
                            // Возвращаемся к выбору файла
                            linkSelected = false;
                            Logger.Log("Возвращаемся к предыдущему шагу", 1);
                            break;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                        }
                    }

                    if (!levelSelected)
                    {
                        // Повторяем выбор файла с начала цикла
                        continue;
                    }

                    Logger.Log("3. Выбор семейства из связанного файла (Семейство А)", 1);
                    // 3. Выбор семейства из связанного файла (Семейство А) с учетом уровня
                    Family selectedFamilyA = null;
                    int instanceCountOnLevel = 0;
                    bool familyASelected = false;

                    while (!familyASelected)
                    {
                        LinkedFamilySelectionForm familyAForm = new LinkedFamilySelectionForm(linkDoc, selectedLevel);
                        var familyAResult = familyAForm.ShowDialog();

                        if (familyAResult == DialogResult.OK && familyAForm.SelectedFamily != null)
                        {
                            selectedFamilyA = familyAForm.SelectedFamily;
                            instanceCountOnLevel = familyAForm.InstanceCountOnLevel;
                            familyASelected = true;
                            Logger.Log("Выбрано семейство " + selectedFamilyA.Name + " " + selectedFamilyA.Id.IntegerValue.ToString(), 1);
                        }
                        else if (familyAResult == DialogResult.Abort) // Назад
                        {
                            // Возвращаемся к выбору уровня
                            levelSelected = false;
                            Logger.Log("Возвращаемся к предыдущему шагу", 1);
                            break;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                        }
                    }

                    if (!familyASelected)
                    {
                        // Повторяем выбор уровня
                        continue;
                    }

                    Logger.Log("4. Выбор семейства из текущего файла (Семейство Б)", 1);
                    // 4. Выбор семейства из текущего файла (Семейство Б)
                    Family selectedFamilyB = null;
                    bool familyBSelected = false;

                    while (!familyBSelected)
                    {
                        CurrentFamilySelectionForm familyBForm = new CurrentFamilySelectionForm(doc);
                        var familyBResult = familyBForm.ShowDialog();

                        if (familyBResult == DialogResult.OK && familyBForm.SelectedFamily != null)
                        {
                            selectedFamilyB = familyBForm.SelectedFamily;
                            familyBSelected = true; Logger.Log("Выбрано семейство " + selectedFamilyB.Name + " " + selectedFamilyB.Id.IntegerValue.ToString(), 1);
                        }
                        else if (familyBResult == DialogResult.Abort) // Назад
                        {
                            // Возвращаемся к выбору семейства А
                            familyASelected = false; Logger.Log("Возвращаемся к предыдущему шагу", 1);
                            break;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                        }
                    }

                    if (!familyBSelected)
                    {
                        // Повторяем выбор семейства А
                        continue;
                    }


                    Logger.Log("5. Настройка расстояния", 1);
                    // 5. Настройка расстояния
                    double distance = 0.5;
                    XYZ direction = XYZ.BasisZ;
                    bool distanceSet = false;

                    while (!distanceSet)
                    {
                        DistanceSettingsForm distanceForm = new DistanceSettingsForm();
                        var distanceResult = distanceForm.ShowDialog();

                        if (distanceResult == DialogResult.OK)
                        {
                            distance = distanceForm.Distance; Logger.Log("Расстояние: " + distance.ToString(), 1);
                            direction = distanceForm.Direction;
                            distanceSet = true;
                        }
                        else if (distanceResult == DialogResult.Abort) // Назад
                        {
                            // Возвращаемся к выбору семейства Б
                            familyBSelected = false; Logger.Log("Возвращаемся к предыдущему шагу", 1);
                            break;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                        }
                    }

                    if (!distanceSet)
                    {
                        // Повторяем выбор семейства Б
                        continue;
                    }

                    // Проверка на слишком малое расстояние
                    if (Math.Abs(distance) < 0.001)
                    {
                        TaskDialog.Show("Внимание",
                            "Расстояние слишком мало! Элементы будут размещены в одной точке.\n" +
                            "Рекомендуется использовать расстояние не менее 0.01 м."); Logger.Log("   Элементы будут размещены в одной точке", 1);
                    }


                    Logger.Log("6. Подтверждение операции", 1);
                    // 6. Подтверждение операции
                    bool confirmed = false;

                    while (!confirmed)
                    {
                        ConfirmationForm confirmForm = new ConfirmationForm(
                            selectedFamilyA != null ? selectedFamilyA.Name : "Не выбрано",
                            selectedFamilyB != null ? selectedFamilyB.Name : "Не выбрано",
                            instanceCountOnLevel,
                            $"{distance} м",
                            selectedLevel?.Name ?? "Не определен"
                        );

                        var confirmResult = confirmForm.ShowDialog();

                        if (confirmResult == DialogResult.OK && confirmForm.UserConfirmed)
                        {
                            confirmed = true;
                        }
                        else if (confirmResult == DialogResult.Abort) // Назад
                        {
                            // Возвращаемся к настройке расстояния
                            distanceSet = false; Logger.Log("Возвращаемся к предыдущему шагу", 1);
                            break;
                        }
                        else // Отмена
                        {
                            Logger.Log("Операция отменена пользователем. Завершение работы.", 3); return Result.Cancelled;
                        }
                    }

                    if (!confirmed)
                    {
                        // Повторяем настройку расстояния
                        continue;
                    }


                    // 7. Выполнение размещения
                    Transaction transaction = new Transaction(doc, "Pikachu Plugin - Размещение элементов");
                    transaction.Start();

                    List<ElementId> createdElements = PlaceElementsNearLinkedInstances(
                        doc, linkDoc, selectedLink, selectedFamilyA, selectedFamilyB, distance, direction, selectedLevel);

                    transaction.Commit();

                    // 8. Показ результатов
                    if (createdElements.Count > 0)
                    {
                        ResultForm resultForm = new ResultForm(createdElements);
                        resultForm.ShowDialog();
                    }
                    else
                    {
                        string mes8 = "Размещение завершено, но новых элементов не было создано.\n" +
                            "Возможно, все элементы уже существуют на правильных позициях.";
                        TaskDialog.Show("Результат",
                            mes8); Logger.Log(mes8, 1);
                    }

                    // Завершаем работу плагина
                    shouldContinue = false;
                }
                Logger.Log("Завершение работы", 5);
                return Result.Succeeded;
            }
            catch (NullReferenceException nre)
            {
                message = $"Ошибка NullReferenceException: {nre.Message}\n\nВероятно, одна из форм не была правильно инициализирована.\n\nStack Trace:\n{nre.StackTrace}";
                Debug.WriteLine($"NullReferenceException: {nre.Message}");
                Debug.WriteLine($"Stack Trace: {nre.StackTrace}");
                new InfoWindow280(message).ShowDialog();
                Logger.Log(message, 4);
                return Result.Failed;
            }
            catch (Exception ex)
            {
                message = $"Ошибка: {ex.Message}";
                Debug.WriteLine($"Exception: {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                new InfoWindow280(message).ShowDialog();
                Logger.Log(message, 4);
                return Result.Failed;
            }
        }

        private List<ElementId> PlaceElementsNearLinkedInstances(
            Document doc,
            Document linkDoc,
            RevitLinkInstance linkInstance,
            Family familyA,
            Family familyB,
            double distance,
            XYZ direction,
            Level selectedLevel)
        {
            if (doc == null || linkDoc == null || linkInstance == null || familyA == null || familyB == null)
            {
                throw new ArgumentNullException("Один или несколько параметров равны null!");
            }

            List<ElementId> createdElements = new List<ElementId>();
            List<ExistingElementInfo> existingElementsToCheck = new List<ExistingElementInfo>();

            // 1. НАХОДИМ ВСЕ СУЩЕСТВУЮЩИЕ ЭКЗЕМПЛЯРЫ СЕМЕЙСТВА Б
            var existingInstancesB = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .Where(x => ((FamilyInstance)x).Symbol.Family.Id == familyB.Id)
                .Cast<FamilyInstance>()
                .ToList();

            // 2. НАХОДИМ ВСЕ ЭКЗЕМПЛЯРЫ СЕМЕЙСТВА А В СВЯЗАННОМ ФАЙЛЕ
            var instancesA = new FilteredElementCollector(linkDoc)
                .OfClass(typeof(FamilyInstance))
                .Where(x => ((FamilyInstance)x).Symbol.Family.Id == familyA.Id)
                .Cast<FamilyInstance>();

            // Фильтруем по уровню, если он выбран
            if (selectedLevel != null)
            {
                instancesA = instancesA.Where(i => i.LevelId == selectedLevel.Id);
            }

            var instancesAList = instancesA.ToList();

            // Получаем символы СЕМЕЙСТВА Б
            var symbolsB = familyB.GetFamilySymbolIds()
                .Select(id => doc.GetElement(id) as FamilySymbol)
                .Where(sym => sym != null && sym.IsActive)
                .ToList();

            if (!symbolsB.Any())
            {
                throw new Exception("В выбранном семействе нет активных типов!");
            }

            FamilySymbol symbolToUse = symbolsB.First();

            // Проверяем, есть ли у символа параметр "Уровень"
            bool requiresLevel = symbolToUse.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) != null;

            // Преобразование координат из связанного файла в текущий
            Transform linkTransform = linkInstance.GetTotalTransform();

            int placedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;

            // 3. СОЗДАЕМ СЛОВАРЬ СУЩЕСТВУЮЩИХ ЭЛЕМЕНТОВ Б ПО ID ИСТОЧНИКА
            Dictionary<string, FamilyInstance> existingElementsBySourceId = CreateSourceIdDictionary(existingInstancesB);

            // 4. ОСНОВНОЙ ЦИКЛ ПО ЭЛЕМЕНТАМ А
            foreach (FamilyInstance instanceA in instancesAList)
            {
                try
                {
                    // Получаем ID элемента А
                    string sourceId = instanceA.Id.ToString();
                    Logger.Log("Исходный элемент " + instanceA.Name + " " + instanceA.Id.IntegerValue.ToString(), 2); //расширенные логи

                    // Проверяем, существует ли уже элемент Б для этого источника
                    if (existingElementsBySourceId.TryGetValue(sourceId, out FamilyInstance existingInstanceB))
                    {
                        // Элемент Б уже существует - проверяем координаты
                        var existingInfo = CheckExistingElement(
                            existingInstanceB, instanceA, linkTransform, direction, distance, doc, selectedLevel);

                        if (existingInfo != null)
                        {
                            existingElementsToCheck.Add(existingInfo);
                        }
                        skippedCount++;
                        continue;
                    }

                    // Элемента Б нет - создаем новый
                    LocationPoint location = instanceA.Location as LocationPoint;
                    if (location == null) { Logger.Log("   положение не определено", 2); continue; }

                    XYZ instancePosition = location.Point;
                    XYZ transformedPosition = linkTransform.OfPoint(instancePosition);
                    XYZ placementPoint = transformedPosition + (direction * distance);

                    // Определяем уровень для размещения
                    Level placementLevel = FindCorrespondingLevel(doc, selectedLevel, placementPoint.Z);

                    // Размещаем элемент
                    FamilyInstance newInstance;

                    if (requiresLevel && placementLevel != null)
                    {
                        newInstance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            symbolToUse,
                            placementLevel,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                        );
                    }
                    else
                    {
                        newInstance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            symbolToUse,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                        );

                        // Пытаемся установить уровень вручную, если возможно
                        if (placementLevel != null && newInstance.LevelId != null)
                        {
                            Parameter levelParam = newInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                            if (levelParam != null && !levelParam.IsReadOnly)
                            {
                                levelParam.Set(placementLevel.Id);
                            }
                        }
                    }

                    if (newInstance != null)
                    {
                        // Записываем ID исходного элемента
                        SetSourceIdComment(newInstance, instanceA.Id);
                        createdElements.Add(newInstance.Id);
                        placedCount++;
                        Logger.Log("   создан элемент " + newInstance.Name + " " + newInstance.Id.IntegerValue.ToString(), 2);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Исходный элемент " + instanceA.Name + " " + instanceA.Id.IntegerValue.ToString() + "ошибка: "
                        + ex.Message, 4);

                    errorCount++;
                    Debug.WriteLine($"Ошибка размещения элемента {instanceA.Id}: {ex.Message}");

                    if (errorCount > 10)
                    {
                        TaskDialog.Show("Внимание",
                            $"Слишком много ошибок ({errorCount}). Прекращаем размещение.\n" +
                            $"Успешно размещено: {placedCount} элементов.");
                        break;
                    }
                }
            }

            // 5. ПРОВЕРКА СУЩЕСТВУЮЩИХ ЭЛЕМЕНТОВ
            if (existingElementsToCheck.Count > 0)
            {
                ShowExistingElementsDialog(doc, existingElementsToCheck,
                    placedCount, skippedCount, errorCount, instancesAList.Count);
            }
            else
            {
                // Показываем простой результат, если нет существующих элементов
                TaskDialog.Show("Результаты размещения",
                    $"Всего элементов А: {instancesAList.Count}\n" +
                    $"Создано новых элементов Б: {placedCount}\n" +
                    $"Пропущено (уже существуют): {skippedCount}\n" +
                    $"Ошибок: {errorCount}");
            }

            return createdElements;
        }

        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ

        private Dictionary<string, FamilyInstance> CreateSourceIdDictionary(List<FamilyInstance> instancesB)
        {
            var dictionary = new Dictionary<string, FamilyInstance>();

            foreach (var instance in instancesB)
            {
                string sourceId = GetSourceIdFromComment(instance);
                if (!string.IsNullOrEmpty(sourceId))
                {
                    dictionary[sourceId] = instance;
                }
            }

            return dictionary;
        }

        private string GetSourceIdFromComment(FamilyInstance instance)
        {
            try
            {
                Parameter commentParam = instance.LookupParameter("Комментарии");
                if (commentParam == null)
                {
                    commentParam = instance.LookupParameter("Comments");
                }

                if (commentParam != null && commentParam.HasValue)
                {
                    string comment = commentParam.AsString();
                    // Ищем паттерн "ID исходного элемента: XXXXX"
                    if (comment.Contains("ID исходного элемента:"))
                    {
                        string[] parts = comment.Split(':');
                        if (parts.Length > 1)
                        {
                            return parts[1].Trim();
                        }
                    }
                    // Ищем старый формат "Источник: XXXXX"
                    else if (comment.Contains("Источник:"))
                    {
                        string[] parts = comment.Split(':');
                        if (parts.Length > 1)
                        {
                            return parts[1].Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка чтения комментария у элемента {instance.Id}: {ex.Message}");
            }

            return null;
        }

        private ExistingElementInfo CheckExistingElement(
            FamilyInstance existingInstanceB,
            FamilyInstance sourceInstanceA,
            Transform linkTransform,
            XYZ direction,
            double distance,
            Document doc,
            Level selectedLevel)
        {
            try
            {
                // Получаем позицию исходного элемента А
                LocationPoint locationA = sourceInstanceA.Location as LocationPoint;
                if (locationA == null) return null;

                XYZ instancePosition = locationA.Point;
                XYZ transformedPosition = linkTransform.OfPoint(instancePosition);
                XYZ expectedPosition = transformedPosition + (direction * distance);

                // Получаем фактическую позицию элемента Б
                LocationPoint locationB = existingInstanceB.Location as LocationPoint;
                if (locationB == null) return null;

                XYZ actualPosition = locationB.Point;

                // Сравниваем позиции с допуском 10 мм
                double tolerance = 0.01; // 10 мм в метрах
                bool isInCorrectPosition = actualPosition.DistanceTo(expectedPosition) <= tolerance;

                // Определяем уровень
                Level placementLevel = FindCorrespondingLevel(doc, selectedLevel, expectedPosition.Z);
                string levelName = placementLevel?.Name ?? "Не определен";

                return new ExistingElementInfo
                {
                    ExistingInstance = existingInstanceB,
                    SourceInstanceId = sourceInstanceA.Id,
                    ExpectedPosition = expectedPosition,
                    ActualPosition = actualPosition,
                    IsInCorrectPosition = isInCorrectPosition,
                    LevelName = levelName
                };
            }
            catch
            {
                return null;
            }
        }

        private void ShowExistingElementsDialog(
            Document doc,
            List<ExistingElementInfo> existingElements,
            int placedCount,
            int skippedCount,
            int errorCount,
            int totalElementsA)
        {
            int correctlyPlaced = existingElements.Count(e => e.IsInCorrectPosition);
            int incorrectlyPlaced = existingElements.Count - correctlyPlaced;

            StringBuilder message = new StringBuilder();
            message.AppendLine("ПРОВЕРКА СУЩЕСТВУЮЩИХ ЭЛЕМЕНТОВ");
            message.AppendLine("================================");
            message.AppendLine($"Всего элементов А: {totalElementsA}");
            message.AppendLine($"Создано новых элементов Б: {placedCount}");
            message.AppendLine($"Пропущено (уже существуют): {skippedCount}");
            message.AppendLine($"Ошибок: {errorCount}");
            message.AppendLine();
            message.AppendLine($"Существующие элементы Б:");
            message.AppendLine($"  • На правильных позициях: {correctlyPlaced}");
            message.AppendLine($"  • На неправильных позициях: {incorrectlyPlaced}");

            // Если есть элементы на неправильных позициях, предлагаем заменить
            if (incorrectlyPlaced > 0)
            {
                TaskDialog dialog = new TaskDialog("Проверка существующих элементов");
                dialog.MainInstruction = message.ToString();
                dialog.MainContent = $"Найдено {incorrectlyPlaced} элементов, которые находятся не на нужных позициях. Хотите их заменить?";

                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    $"Да, заменить {incorrectlyPlaced} элементов на новые");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Нет, оставить как есть");
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                    "Показать подробный список элементов");

                TaskDialogResult result = dialog.Show();

                if (result == TaskDialogResult.CommandLink1)
                {
                    // Заменяем элементы
                    ReplaceIncorrectElements(doc, existingElements.Where(e => !e.IsInCorrectPosition).ToList());
                }
                else if (result == TaskDialogResult.CommandLink3)
                {
                    // Показываем подробный список
                    ShowDetailedElementsList(existingElements);

                    // Спрашиваем еще раз после показа списка
                    TaskDialog followUpDialog = new TaskDialog("Замена элементов");
                    followUpDialog.MainInstruction = "Хотите заменить элементы на неправильных позициях?";
                    followUpDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                        $"Да, заменить {incorrectlyPlaced} элементов");
                    followUpDialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                        "Нет, оставить как есть");

                    if (followUpDialog.Show() == TaskDialogResult.CommandLink1)
                    {
                        ReplaceIncorrectElements(doc, existingElements.Where(e => !e.IsInCorrectPosition).ToList());
                    }
                }
            }
            else
            {
                TaskDialog.Show("Результаты размещения", message.ToString());
            }
        }

        private void ReplaceIncorrectElements(Document doc, List<ExistingElementInfo> elementsToReplace)
        {
            int replacedCount = 0;
            int errorCount = 0;

            foreach (var elementInfo in elementsToReplace)
            {
                try
                {
                    // Сохраняем символ старого элемента
                    FamilySymbol oldSymbol = elementInfo.ExistingInstance.Symbol;

                    // Удаляем старый элемент
                    doc.Delete(elementInfo.ExistingInstance.Id);

                    // Создаем новый на правильной позиции
                    FamilyInstance newInstance;

                    // Проверяем, нужен ли уровень
                    bool requiresLevel = oldSymbol.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) != null;
                    Level level = FindLevelByName(doc, elementInfo.LevelName);

                    if (requiresLevel && level != null)
                    {
                        newInstance = doc.Create.NewFamilyInstance(
                            elementInfo.ExpectedPosition,
                            oldSymbol,
                            level,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                        );
                    }
                    else
                    {
                        newInstance = doc.Create.NewFamilyInstance(
                            elementInfo.ExpectedPosition,
                            oldSymbol,
                            Autodesk.Revit.DB.Structure.StructuralType.NonStructural
                        );

                        // Устанавливаем уровень вручную, если нужно
                        if (level != null)
                        {
                            Parameter levelParam = newInstance.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
                            if (levelParam != null && !levelParam.IsReadOnly)
                            {
                                levelParam.Set(level.Id);
                            }
                        }
                    }

                    // Копируем параметры из старого элемента (кроме комментария)
                    CopyParameters(elementInfo.ExistingInstance, newInstance);

                    // Восстанавливаем ID источника в комментарии
                    SetSourceIdComment(newInstance, elementInfo.SourceInstanceId);

                    replacedCount++;
                }
                catch (Exception ex)
                {
                    errorCount++;
                    Debug.WriteLine($"Ошибка замены элемента: {ex.Message}");
                }
            }

            TaskDialog.Show("Замена элементов",
                $"Заменено элементов: {replacedCount}\n" +
                $"Ошибок при замене: {errorCount}");
        }

        private void CopyParameters(FamilyInstance source, FamilyInstance target)
        {
            try
            {
                // Не копируем комментарии, т.к. они будут установлены заново
                foreach (Parameter sourceParam in source.Parameters)
                {
                    if (sourceParam.IsReadOnly || !sourceParam.HasValue) continue;

                    // Пропускаем параметры с комментариями
                    if (sourceParam.Definition.Name == "Комментарии" ||
                        sourceParam.Definition.Name == "Comments" ||
                        sourceParam.Definition.Name == "ALL_MODEL_INSTANCE_COMMENTS")
                        continue;

                    Parameter targetParam = target.LookupParameter(sourceParam.Definition.Name);
                    if (targetParam != null && !targetParam.IsReadOnly)
                    {
                        try
                        {
                            switch (sourceParam.StorageType)
                            {
                                case StorageType.String:
                                    targetParam.Set(sourceParam.AsString());
                                    break;
                                case StorageType.Integer:
                                    targetParam.Set(sourceParam.AsInteger());
                                    break;
                                case StorageType.Double:
                                    targetParam.Set(sourceParam.AsDouble());
                                    break;
                                case StorageType.ElementId:
                                    ElementId elemId = sourceParam.AsElementId();
                                    if (elemId != null && elemId != ElementId.InvalidElementId)
                                    {
                                        targetParam.Set(elemId);
                                    }
                                    break;
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки копирования отдельных параметров
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка копирования параметров: {ex.Message}");
            }
        }

        private void ShowDetailedElementsList(List<ExistingElementInfo> elements)
        {
            // Создаем форму для отображения списка элементов
            using (var form = new System.Windows.Forms.Form())
            {
                form.Text = "Подробный список элементов";
                form.Size = new System.Drawing.Size(900, 600);
                form.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

                var listView = new System.Windows.Forms.ListView();
                listView.Dock = System.Windows.Forms.DockStyle.Fill;
                listView.View = System.Windows.Forms.View.Details;
                listView.FullRowSelect = true;
                listView.GridLines = true;

                listView.Columns.Add("ID элемента", 100);
                listView.Columns.Add("Статус", 150);
                listView.Columns.Add("Уровень", 150);
                listView.Columns.Add("Ожидаемая позиция", 200);
                listView.Columns.Add("Фактическая позиция", 200);
                listView.Columns.Add("Разница, мм", 100);

                foreach (var element in elements)
                {
                    double difference = element.ActualPosition.DistanceTo(element.ExpectedPosition) * 1000; // в мм

                    var item = new System.Windows.Forms.ListViewItem(element.ExistingInstance.Id.ToString());
                    item.SubItems.Add(element.IsInCorrectPosition ? "На месте ✓" : "Не на месте ✗");
                    item.SubItems.Add(element.LevelName);
                    item.SubItems.Add($"X:{element.ExpectedPosition.X:F3}, Y:{element.ExpectedPosition.Y:F3}");
                    item.SubItems.Add($"X:{element.ActualPosition.X:F3}, Y:{element.ActualPosition.Y:F3}");
                    item.SubItems.Add($"{difference:F1}");

                    if (!element.IsInCorrectPosition)
                    {
                        item.ForeColor = System.Drawing.Color.Red;
                        item.Font = new System.Drawing.Font(listView.Font, System.Drawing.FontStyle.Bold);
                    }
                    else
                    {
                        item.ForeColor = System.Drawing.Color.Green;
                    }

                    listView.Items.Add(item);
                }

                // Добавляем кнопки
                var panel = new System.Windows.Forms.Panel();
                panel.Dock = System.Windows.Forms.DockStyle.Bottom;
                panel.Height = 40;

                var closeButton = new System.Windows.Forms.Button();
                closeButton.Text = "Закрыть";
                closeButton.DialogResult = System.Windows.Forms.DialogResult.OK;
                closeButton.Anchor = System.Windows.Forms.AnchorStyles.Right;
                closeButton.Location = new System.Drawing.Point(800, 10);

                panel.Controls.Add(closeButton);

                form.Controls.Add(listView);
                form.Controls.Add(panel);
                form.ShowDialog();
            }
        }

        private Level FindLevelByName(Document doc, string levelName)
        {
            if (string.IsNullOrEmpty(levelName))
                return null;

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == levelName);

            return level;
        }

        private Level FindCorrespondingLevel(Document doc, Level sourceLevel, double elevation)
        {
            if (sourceLevel == null)
            {
                // Ищем ближайший уровень по отметке
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => Math.Abs(l.Elevation - elevation))
                    .ToList();

                return levels.FirstOrDefault();
            }

            // Ищем уровень с таким же именем
            var levelByName = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name == sourceLevel.Name);

            if (levelByName != null) return levelByName;

            // Ищем уровень с близкой отметкой
            var levelByElevation = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - sourceLevel.Elevation))
                .FirstOrDefault();

            return levelByElevation;
        }

        private void SetSourceIdComment(FamilyInstance newInstance, ElementId sourceId)
        {
            try
            {
                // Пытаемся найти параметр "Комментарии"
                Parameter commentParam = newInstance.LookupParameter("Комментарии");
                if (commentParam == null)
                {
                    // Пробуем английское название
                    commentParam = newInstance.LookupParameter("Comments");
                }

                if (commentParam != null && !commentParam.IsReadOnly)
                {
                    // Записываем ID исходного элемента
                    string comment = $"ID исходного элемента: {sourceId}";
                    commentParam.Set(comment);
                }
                else
                {
                    // Если параметр не найден, создаем shared parameter или используем другой
                    Parameter noteParam = newInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (noteParam != null && !noteParam.IsReadOnly)
                    {
                        noteParam.Set($"ID исходного элемента: {sourceId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось записать ID источника: {ex.Message}");
            }
        }

        // КЛАСС ДЛЯ ХРАНЕНИЯ ИНФОРМАЦИИ О СУЩЕСТВУЮЩИМ ЭЛЕМЕНТЕ
        private class ExistingElementInfo
        {
            public FamilyInstance ExistingInstance { get; set; }
            public ElementId SourceInstanceId { get; set; }
            public XYZ ExpectedPosition { get; set; }
            public XYZ ActualPosition { get; set; }
            public bool IsInCorrectPosition { get; set; }
            public string LevelName { get; set; }
        }
    }
}