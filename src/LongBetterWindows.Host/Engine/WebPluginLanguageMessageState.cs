namespace LongBetterWindows.Host.Engine;

internal sealed class WebPluginLanguageMessageState
{
    private string? _currentMessage;
    private bool _navigationReady;

    public string? Update(string message)
    {
        _currentMessage = message;
        return _navigationReady ? message : null;
    }

    public void BeginNavigation() => _navigationReady = false;

    public string? CompleteNavigation(bool isSuccess)
    {
        _navigationReady = isSuccess;
        return isSuccess ? _currentMessage : null;
    }
}
