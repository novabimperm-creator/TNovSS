using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;
using TNovCommon;

namespace TNovSS
{
    public class SSSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            bool result = false;
            switch (element.Category.Name)
            {
                case "Пожарная сигнализация": result = true; break;
                case "Электрооборудование": result = true; break;
                case "Устройства вызова и оповещения": result = true; break;
                case "Устройства связи": result = true; break;
            }
            return result;
        }

        public bool AllowReference(Reference refer, XYZ point)
        {
            return false;
        }
    }
    
    [Transaction(TransactionMode.Manual)]
    public class SSNumberer : IExternalCommand
    {
        Guid adskPparamGuid = new Guid("ae8ff999-1f22-4ed7-ad33-61503d85f0f4");//ADSK_Позиция
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {

            #region Исходные
            DateTime dateTime = DateTime.Now;
            string TNovVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            string DBCommandName = "Адресатор";
            //подключение приложения и документа
            if (RevitAPI.UiApplication == null) { RevitAPI.Initialize(commandData); }
            UIDocument uidoc = RevitAPI.UiDocument; Document doc = RevitAPI.Document;
            UIApplication uiApp = RevitAPI.UiApplication; Autodesk.Revit.ApplicationServices.Application rvtApp = uiApp.Application;
            string docName = doc.Title.ToString(); docName = docName.Replace(",", " ");
            string userName = rvtApp.Username; userName = userName.Replace(",", "");
            string docNameUserName = "_" + userName; docName = docName.Replace(docNameUserName, "");
            docName = docName.Replace(",", "");
            #endregion

            TNovConfig config = TNovConfigLoad.LoadConfig(DBCommandName, TNovVersion);

            #region Настройки логов
            // создание log - файла
            Logger.Initialize(DBCommandName, dateTime, TNovVersion);

            var viewModel0 = new AppVersionViewModel();

            string jsonpath0 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "TNovClient/TNovSettings.json");
            viewModel0 = JsonConvert.DeserializeObject<AppVersionViewModel>(File.ReadAllText(jsonpath0));
            if (viewModel0.extendedLogs)

            {
                var qViewModel = new QuestionWindowViewModel();
                qViewModel.headtxt = "Включены расширенные логи. " +
                    "Плагин будет работать медленнее, но соберет больше данных. " +
                    "Выключить расширенные логи для ускорения работы?";
                var qwpfview = new QuestionWindow280(qViewModel);
                qViewModel.CloseRequest += (s, e) => qwpfview.Close();
                bool? qok = qwpfview.ShowDialog();
                if (qok != null && qok == true) { Logger.TurnOffExtendedLogs(); } else Logger.Log("Расширенные логи вкл", 2);
            }
            #endregion

            #region Диалог
            var viewModel = new SSNumbererViewModel();
            // Десериализация
            bool forProject = true;
            json js = new json(in DBCommandName, in forProject, out bool canserialize, out string jsonpath);
            if (canserialize)
            {
                viewModel = JsonConvert.DeserializeObject<SSNumbererViewModel>(File.ReadAllText(jsonpath));
                Logger.Log("Десериализация прошла успешно", 1);
            }
            var view = new SSNumbererWPF(viewModel);
            viewModel.CloseRequest += (s, e) => view.Close();
            viewModel.HideRequest += (s, e) => view.Hide();
            viewModel.ShowRequest += (s, e) => view.ShowDialog();
            view.ShowDialog();
            //Сериализация
            try
            {
                File.WriteAllText(jsonpath, JsonConvert.SerializeObject(viewModel));
                Logger.Log("Сериализация прошла успешно", 1);
            }
            catch (Exception ex) { Logger.Log("Ошибка при сериализации: " + ex.Message, 4); }
            #endregion

            #region Финал
            //получаем элементы
            List<FamilyInstance> FIs = new List<FamilyInstance>();
            List<FamilyInstance> elEq = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_ElectricalEquipment)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList(); foreach (FamilyInstance el in elEq) FIs.Add(el);
            List<FamilyInstance> FireAlarmDevices = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_FireAlarmDevices)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList(); foreach (FamilyInstance fad in FireAlarmDevices) FIs.Add(fad);
            List<FamilyInstance> AlertDevices = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_NurseCallDevices)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList(); foreach (FamilyInstance ad in AlertDevices) FIs.Add(ad);
            List<FamilyInstance> CommDevices = new FilteredElementCollector(RevitAPI.Document).OfCategory(BuiltInCategory.OST_CommunicationDevices)
                .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList(); foreach (FamilyInstance cd in CommDevices) FIs.Add(cd);
            //ищем элементы цепи
            string curcuit = viewModel.circuitvalue; if (viewModel.circuitsection != "") curcuit = viewModel.circuitvalue + "." + viewModel.circuitsection;
            List<int> numbersList = new List<int>();
            foreach (var FI in FIs)
            {
                string mark = FI.get_Parameter(BuiltInParameter.DOOR_NUMBER).AsString();
                Element elem = RevitAPI.Document.GetElement(FI.Id); Element type = RevitAPI.Document.GetElement(elem.GetTypeId());
                //параметр-префикс
                string adskP = "";
                if (Param.ParamExistByGuid(adskPparamGuid, elem))
                    adskP = elem.get_Parameter(adskPparamGuid).AsString();
                else if (Param.ParamExistByGuid(adskPparamGuid, type))
                    adskP = type.get_Parameter(adskPparamGuid).AsString();
                if (adskP != "" && adskP != null) mark = mark.Replace(adskP, ""); //убираем из Марки префикс (ADSK_Позиция)
                //получаем из Марки цепь
                string[] markparts = mark.Split('.');
                if (markparts.Length > 1) mark = mark.Replace("." + markparts[markparts.Length - 1], "");
                //проверяем цепь на введенную в окне
                if (mark == curcuit)
                {
                    int i = 0;
                    int.TryParse(markparts[markparts.Length - 1], out i);
                    numbersList.Add(i);
                }
            }
            if (numbersList.Count > 0)
            {
                string messageStart = "В цепи " + curcuit;
                string message1 = "";
                //проверяем наличие пропусков в нумерации элементов цепи
                int maxNumber = numbersList.Max();
                if (maxNumber > numbersList.Count + 1)
                    message1 = " "+numbersList.Count.ToString() +
                        " элементов, а последний использованный номер - " + numbersList.Max().ToString();
                string message2 = "";
                //проверяем наличие дублей
                var duplicates = numbersList.GroupBy(s => s.ToString()).SelectMany(grp => grp.Skip(1));
                if(duplicates.ToList().Count> 0)
                {
                    duplicates = duplicates.Distinct().ToList();
                    string combinedString = string.Join(",", duplicates.ToArray());
                    if (message1.Length > 0) message2 = ", а также";
                    message2 += " найдены дублирующиеся адреса: " + combinedString;
                }
                if(duplicates.ToList().Count > 0|| maxNumber > numbersList.Count + 1)
                {
                    new InfoWindow280(messageStart + message1+ message2+
                        ". Возможно, нужна перенумерация элементов.").ShowDialog();
                }
            }
            #endregion
            Logger.Log("Завершение работы.", 5);
            return Result.Succeeded;
        }
    }
}
