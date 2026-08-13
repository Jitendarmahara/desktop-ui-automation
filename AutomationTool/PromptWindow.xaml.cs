using System.Windows;

namespace AutomationTool;

public partial class PromptWindow : Window
{
    public string ResponseText { get; private set; } = string.Empty;

    public PromptWindow(string title, string prompt)
    {
        InitializeComponent();
        WindowForegroundHelper.BringToFront(this);
        Title = title;
        PromptText.Text = prompt;
        Loaded += (_, _) => InputBox.Focus();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        ResponseText = InputBox.Text;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
