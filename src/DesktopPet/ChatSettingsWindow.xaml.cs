using System.Windows;
using DesktopPet.Core;

namespace DesktopPet;

public partial class ChatSettingsWindow : Window
{
    private readonly ChatSettingsStore _store;
    public ChatSettings? SavedSettings { get; private set; }

    public ChatSettingsWindow(ChatSettings settings, ChatSettingsStore store)
    {
        InitializeComponent();
        settings = settings.Normalize();
        _store = store;
        ApiKeyBox.Password = settings.ApiKey;
        ModelBox.Text = settings.Model;
        PersonaBox.Text = settings.Persona;
        PresentationBox.SelectedValue = settings.DefaultPresentation.ToString();
        MessageCountBox.SelectedValue = settings.BubbleMessageCount.ToString();
        DismissBox.SelectedValue = settings.BubbleDismiss.ToString();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyBox.Password)) { StatusText.Text = "请填写 DeepSeek API 密钥。"; return; }
        if (string.IsNullOrWhiteSpace(ModelBox.Text)) { StatusText.Text = "请填写模型名称。"; return; }
        if (string.IsNullOrWhiteSpace(PersonaBox.Text)) { StatusText.Text = "请填写她的性格和聊天方式。"; return; }
        var settings = new ChatSettings
        {
            ApiKey = ApiKeyBox.Password.Trim(),
            Model = ModelBox.Text.Trim(),
            Persona = PersonaBox.Text.Trim(),
            DefaultPresentation = (ChatPresentationMode)Enum.Parse(typeof(ChatPresentationMode), (string)PresentationBox.SelectedValue),
            BubbleMessageCount = int.Parse((string)MessageCountBox.SelectedValue),
            BubbleDismiss = (BubbleDismissMode)Enum.Parse(typeof(BubbleDismissMode), (string)DismissBox.SelectedValue)
        }.Normalize();
        try { _store.Save(settings); }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            StatusText.Text = "设置未保存，请检查本机文件访问权限后重试。";
            return;
        }
        SavedSettings = settings;
        if (IsVisible && Owner is not null) DialogResult = true;
    }
}
