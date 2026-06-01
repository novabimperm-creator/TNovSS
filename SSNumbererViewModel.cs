using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using TNovCommon;

namespace TNovSS
{
    public class SSNumbererViewModel : INotifyPropertyChanged
    {
        Guid adskPparamGuid = new Guid("ae8ff999-1f22-4ed7-ad33-61503d85f0f4");//ADSK_Позиция
        private string _startvalue = "1";
        public string startvalue { get => _startvalue; set { _startvalue = value; OnPropertyChanged(); } } //можно обновить из окна
        private string _circuitvalue = "1/1";
        public string circuitvalue { get => _circuitvalue; set { _circuitvalue = value; OnPropertyChanged(); } }
        private string _circuitsection = "";
        public string circuitsection { get => _circuitsection; set { _circuitsection = value; OnPropertyChanged(); } }

        public RelayCommand NumerateCommand { get; set; }
        public RelayCommand GetLastNumberCommand { get; set; }
        public SSNumbererViewModel()
        {
            NumerateCommand = new RelayCommand(param => { Numerate(); }, CanNumerate); //команда метода нумерации
            GetLastNumberCommand = new RelayCommand(param => { GetLastNumber(); }, CanGetLastNumber); //команда подгрузки последнего номера в цепи-выходе
        }
        public void Numerate() //метод нумерации
        {
            RaiseHideRequest();
            int i = 1;
            int.TryParse(startvalue, out i);
            string prefix = circuitvalue + ".";
            if (circuitsection != "") prefix = prefix + circuitsection + ".";
            if (i > 200)
            {
                new InfoWindow280($"Ошибка!\nПревышено максимальное количество элементов в цепи (200).").ShowDialog();
            }
            using (TransactionGroup group = new TransactionGroup(RevitAPI.Document, "TNov - Адресатор"))
            {
                ISelectionFilter _filter = new SSSelectionFilter();
                group.Start();

                while (i < 201)
                {
                    try
                    {
                        using (Transaction t = new Transaction(RevitAPI.Document, "TNov - Адресатор"))
                        {
                            t.Start();
                            TransactionHandler.SetWarningResolver(t);
                            Reference reference = RevitAPI.UiDocument.Selection.PickObject(ObjectType.Element, _filter, $"Выберите элемент {i}");
                            Element elem = RevitAPI.Document.GetElement(reference); Element type = RevitAPI.Document.GetElement(elem.GetTypeId());
                            string prefix1 = prefix;
                            //параметр-префикс
                            if (Param.ParamExistByGuid(adskPparamGuid, elem))
                                prefix1 = elem.get_Parameter(adskPparamGuid).AsString() + prefix1;
                            else if (Param.ParamExistByGuid(adskPparamGuid, type))
                                prefix1 = type.get_Parameter(adskPparamGuid).AsString() + prefix1;
                            //целевой параметр
                            Autodesk.Revit.DB.Parameter parameter = elem.get_Parameter(BuiltInParameter.DOOR_NUMBER);
                            if (parameter != null)
                            {
                                parameter.Set(prefix1 + i.ToString());
                                i++;
                                t.Commit();
                            }
                            else
                            {
                                t.Commit();
                                group.Assimilate();
                                break;
                            }
                        }
                    }
                    catch
                    {
                        group.Assimilate();
                        break;
                    }
                }
            }
            startvalue = i.ToString();
            RaiseShowRequest();
        }
        public void GetLastNumber() //метод получения последнего номера в цепи-выходе
        {
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
            string curcuit = circuitvalue; if (circuitsection != "") curcuit = circuitvalue + "." + circuitsection;
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
                //проверяем наличие пропусков в нумерации элементов цепи
                int maxNumber = numbersList.Max();
                if (maxNumber > numbersList.Count + 1)
                    new InfoWindow280("В цепи " + curcuit + " " + numbersList.Count.ToString() +
                        " элементов, а последний использованный номер - " + numbersList.Max().ToString() +
                        ". Брать последний номер - некорректно. Возможно, нужна перенумерация элементов.").ShowDialog();
                else
                {
                    //назначаем стартовый номер
                    if (maxNumber > 0) { maxNumber = maxNumber + 1; startvalue = maxNumber.ToString(); }
                }
            }
            else startvalue = "1";



        }
        private bool CanNumerate(object param)
        {
            return int.TryParse(startvalue, out _);
        }
        private bool CanGetLastNumber(object param)
        {
            /*
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
            //ищем цепь по введенным данным
            string curcuit = circuitvalue; if (circuitsection != "") curcuit = circuitvalue + "." + circuitsection;
            bool curcuitExists = false;
            foreach(var FI in FIs)
            {
                string mark = FI.get_Parameter(BuiltInParameter.DOOR_NUMBER).AsString();
                Element elem = RevitAPI.Document.GetElement(FI.Id); Element type = RevitAPI.Document.GetElement(elem.GetTypeId());
                //параметр-префикс
                string adskP = "";
                if (param.ParamExistByGuid(adskPparamGuid, elem))
                    adskP = elem.get_Parameter(adskPparamGuid).AsString();
                else if (param.ParamExistByGuid(adskPparamGuid, type))
                    adskP = type.get_Parameter(adskPparamGuid).AsString();
                if (adskP != "") mark = mark.Replace(adskP, ""); //убираем из Марки префикс (ADSK_Позиция)
                //получаем из Марки цепь
                string[] markparts = mark.Split('.');
                if (markparts.Length > 2) mark = mark.Replace("." + markparts[markparts.Length - 1], "");
                //проверяем цепь на введенную в окне
                if (mark == curcuit) { curcuitExists = true; break; }
            }
            //цепь существует
            return curcuitExists;
            */
            return true;
        }

        public event EventHandler CloseRequest;
        private void RaiseCloseRequest()
        {
            CloseRequest?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler HideRequest;
        private void RaiseHideRequest()
        {
            HideRequest?.Invoke(this, EventArgs.Empty);
        }
        public event EventHandler ShowRequest;
        private void RaiseShowRequest()
        {
            ShowRequest?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void OnPropertyChanged([CallerMemberName] string PropertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }
    }
}
