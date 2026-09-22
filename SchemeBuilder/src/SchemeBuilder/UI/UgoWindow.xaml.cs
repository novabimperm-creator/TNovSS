using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using SchemeBuilder.Core;
using TNovCommon;

namespace SchemeBuilder.UI
{
    internal partial class UgoWindow : Window
    {
        internal const string NoUgo = "— без УГО —";

        private readonly Document _document;
        private readonly SchemeSettings _settings;
        private readonly List<DeviceRow> _rows;

        private List<UgoCandidate> _candidates = new List<UgoCandidate>();
        private List<UgoMatch> _matches = new List<UgoMatch>();

        public UgoWindow(Document document, SchemeSettings settings, List<DeviceRow> rows)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rows = rows ?? throw new ArgumentNullException(nameof(rows));

            InitializeComponent();
            Fill();
            DataContext = this;
        }

        internal List<UgoMatch> Matches => _matches;

        public List<string> UgoCaptions { get; private set; } = new List<string>();

        public ObservableCollection<UgoRowVm> Rows { get; } = new ObservableCollection<UgoRowVm>();

        internal UgoCandidate FindCandidate(string caption)
        {
            return _candidates.FirstOrDefault(c => c.ToString() == caption);
        }

        internal void RefreshSummary()
        {
            int chosen = _matches.Count(m => m.Selected != null);
            int guessed = _matches.Count(m => m.Selected != null && m.Guessed);

            Summary.Text = "Типоразмеров: " + _matches.Count.ToString(CultureInfo.CurrentCulture) +
                           ", с УГО: " + chosen.ToString(CultureInfo.CurrentCulture) +
                           " (подставлено плагином: " + guessed.ToString(CultureInfo.CurrentCulture) + ")" +
                           ", без УГО: " + (_matches.Count - chosen).ToString(CultureInfo.CurrentCulture) +
                           ".   Аннотаций в проекте: " + _candidates.Count.ToString(CultureInfo.CurrentCulture);
        }

        private void Fill()
        {
            if (_candidates.Count == 0) _candidates = UgoFinder.Candidates(_document);
            if (_matches.Count == 0) _matches = UgoFinder.Match(_document, _rows, _candidates, _settings);

            UgoCaptions = new List<string> { NoUgo };
            UgoCaptions.AddRange(_candidates.Select(c => c.ToString()));

            Rows.Clear();
            foreach (UgoMatch match in _matches) Rows.Add(new UgoRowVm(this, match));

            Grid.ItemsSource = Rows;
            RefreshSummary();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            foreach (UgoMatch match in _matches)
            {
                match.Selected = null;
                match.Guessed = false;
            }

            foreach (UgoRowVm row in Rows) row.NotifySelection();
            RefreshSummary();
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void HelpButton_Click(object sender, RoutedEventArgs e)
        {
            HelpLinks.ShowHelp("-");
        }
    }

    internal sealed class UgoRowVm : ObservableObject
    {
        private readonly UgoWindow _owner;

        internal UgoRowVm(UgoWindow owner, UgoMatch match)
        {
            _owner = owner;
            Match = match;
        }

        internal UgoMatch Match { get; }

        public string Code => Match.Row.Code;

        public string TypeName => Match.Row.TypeName;

        public int Count => Match.Row.Count;

        public string Declared => Match.Declared;

        public string SelectedCaption
        {
            get => Match.Selected == null ? UgoWindow.NoUgo : Match.Selected.ToString();
            set
            {
                Match.Selected = string.IsNullOrEmpty(value) || value == UgoWindow.NoUgo
                    ? null
                    : _owner.FindCandidate(value);
                Match.Guessed = false;
                NotifySelection();
                _owner.RefreshSummary();
            }
        }

        public string State
        {
            get
            {
                if (Match.Selected == null) return "нет — блок текстом";
                return Match.Guessed ? "подставлено плагином" : "выбрано";
            }
        }

        internal void NotifySelection()
        {
            OnPropertyChanged(nameof(SelectedCaption));
            OnPropertyChanged(nameof(State));
        }
    }
}
