using System.Windows;

namespace LongBetterWindows.Host.Views
{
    public partial class ScriptCreationDialog : Window
    {
        public string ScriptPath { get; private set; } = string.Empty;
        public string SelectedLanguage { get; private set; } = "JavaScript";
        public string SelectedTemplate { get; private set; } = "热键插件";
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
                MessageBox.Show("请输入文件名", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 移除扩展名（如果用户输入了）
            name = name.Replace(".csx", "").Replace(".js", "").Replace(".ts", "");

            // 验证文件名合法性
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("文件名包含非法字符", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ScriptPath = name;

            // 获取选中的语言
            if (LangCSharp.IsChecked == true)
                SelectedLanguage = "C#";
            else if (LangJS.IsChecked == true)
                SelectedLanguage = "JavaScript";
            else if (LangTS.IsChecked == true)
                SelectedLanguage = "TypeScript";

            // 获取选中的模板
            if (TplHotkey.IsChecked == true)
                SelectedTemplate = "热键插件";
            else if (TplNote.IsChecked == true)
                SelectedTemplate = "笔记插件";
            else if (TplBlank.IsChecked == true)
                SelectedTemplate = "空白";

            OpenInEditor = OpenEditorCheck.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
