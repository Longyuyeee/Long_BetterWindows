using System.Windows;

namespace LongBetterWindows.Host.Views
{
    public partial class ScriptCreationDialog : Window
    {
        public const string HotkeyTemplate = "hotkey";
        public const string NoteTemplate = "note";
        public const string BlankTemplate = "blank";

        public string ScriptPath { get; private set; } = string.Empty;
        public string SelectedLanguage { get; private set; } = "JavaScript";
        public string SelectedTemplate { get; private set; } = HotkeyTemplate;
        public bool OpenInEditor { get; private set; }

        public ScriptCreationDialog()
        {
            InitializeComponent();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            var name = ScriptNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ShowValidationMessage("developer.script.validation.fileNameRequired");
                return;
            }

            name = name.Replace(".csx", "").Replace(".js", "").Replace(".ts", "");

            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                ShowValidationMessage("developer.script.validation.invalidFileName");
                return;
            }

            ScriptPath = name;

            if (LangCSharp.IsChecked == true)
                SelectedLanguage = "C#";
            else if (LangJS.IsChecked == true)
                SelectedLanguage = "JavaScript";
            else if (LangTS.IsChecked == true)
                SelectedLanguage = "TypeScript";

            if (TplHotkey.IsChecked == true)
                SelectedTemplate = HotkeyTemplate;
            else if (TplNote.IsChecked == true)
                SelectedTemplate = NoteTemplate;
            else if (TplBlank.IsChecked == true)
                SelectedTemplate = BlankTemplate;

            OpenInEditor = OpenEditorCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static void ShowValidationMessage(string messageKey)
        {
            MessageBox.Show(
                Services.ServicesInitializer.I18n.T(messageKey),
                Services.ServicesInitializer.I18n.T("developer.script.validation.title"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
