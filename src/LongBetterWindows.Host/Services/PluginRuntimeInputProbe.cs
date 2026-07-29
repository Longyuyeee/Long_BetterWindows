using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;
using LongBetterWindows.Host.Views;
using Microsoft.Web.WebView2.Wpf;

namespace LongBetterWindows.Host.Services
{
    internal sealed record PluginRuntimeInputProbeResult(
        bool ForegroundRequested,
        string ActiveElement,
        bool InputDispatched,
        bool InputReceived,
        string InputObserved,
        bool PageDownDispatched,
        double ScrollBeforeDetach,
        bool ControlDSent);

    internal sealed record PluginRuntimeDomSnapshot(
        string Input,
        double Scroll);

    internal static class PluginRuntimeInputProbe
    {
        internal const string InputValue = "420731";

        internal static async Task<PluginRuntimeInputProbeResult> RunAsync(
            MainWindow mainWindow,
            WebView2 webView)
        {
            var foregroundRequested = QualityKeyboardInput.Activate(
                new WindowInteropHelper(mainWindow).Handle);
            mainWindow.Activate();
            webView.Focus();
            Keyboard.Focus(webView);
            webView.MoveFocus(
                new TraversalRequest(FocusNavigationDirection.First));
            await Task.Delay(100);

            var core = webView.CoreWebView2
                ?? throw new InvalidOperationException(
                    "WebView2 is not initialized.");
            var activeElement = JsonSerializer.Deserialize<string>(
                    await core.ExecuteScriptAsync(
                        """
                        (() => {
                          const input = document.getElementById('input');
                          if (!input) return '';
                          input.value = '';
                          let spacer =
                            document.getElementById('quality-scroll-spacer');
                          if (!spacer) {
                            spacer = document.createElement('div');
                            spacer.id = 'quality-scroll-spacer';
                            spacer.style.height = '1600px';
                            document.body.appendChild(spacer);
                          }
                          window.scrollTo(0, 0);
                          input.focus();
                          return document.activeElement === input
                            ? input.id
                            : '';
                        })()
                        """))
                ?? string.Empty;
            var inputDispatched = false;
            if (activeElement == "input")
            {
                await core.CallDevToolsProtocolMethodAsync(
                    "Input.insertText",
                    JsonSerializer.Serialize(new { text = InputValue }));
                inputDispatched = true;
            }

            var inputObserved = string.Empty;
            var inputReceived = await WaitUntilAsync(
                async () =>
                {
                    inputObserved = JsonSerializer.Deserialize<string>(
                            await core.ExecuteScriptAsync(
                                "document.getElementById('input')?.value ?? ''"))
                        ?? string.Empty;
                    return inputObserved == InputValue;
                },
                5_000);

            await core.ExecuteScriptAsync(
                """
                (() => {
                  document.body.tabIndex = -1;
                  document.body.focus();
                  window.scrollTo(0, 0);
                })()
                """);
            await Task.Delay(100);
            var pageDownDispatched = false;
            if (inputReceived)
            {
                await DispatchPageDownAsync(core);
                pageDownDispatched = true;
            }

            var scrollBeforeDetach = 0d;
            await WaitUntilAsync(
                async () =>
                {
                    scrollBeforeDetach = JsonSerializer.Deserialize<double>(
                        await core.ExecuteScriptAsync("window.scrollY"));
                    return scrollBeforeDetach > 0;
                },
                5_000);
            var controlDSent = pageDownDispatched
                && foregroundRequested
                && QualityKeyboardInput.SendControlD();
            return new PluginRuntimeInputProbeResult(
                foregroundRequested,
                activeElement,
                inputDispatched,
                inputReceived,
                inputObserved,
                pageDownDispatched,
                scrollBeforeDetach,
                controlDSent);
        }

        internal static async Task<PluginRuntimeDomSnapshot> CaptureAsync(
            WebView2 webView)
        {
            var core = webView.CoreWebView2
                ?? throw new InvalidOperationException(
                    "WebView2 is not initialized.");
            return new PluginRuntimeDomSnapshot(
                JsonSerializer.Deserialize<string>(
                    await core.ExecuteScriptAsync(
                        "document.getElementById('input')?.value ?? ''"))
                    ?? string.Empty,
                JsonSerializer.Deserialize<double>(
                    await core.ExecuteScriptAsync("window.scrollY")));
        }

        private static async Task DispatchPageDownAsync(
            Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            foreach (var type in new[] { "keyDown", "keyUp" })
            {
                await core.CallDevToolsProtocolMethodAsync(
                    "Input.dispatchKeyEvent",
                    JsonSerializer.Serialize(new
                    {
                        type,
                        key = "PageDown",
                        code = "PageDown",
                        windowsVirtualKeyCode = 34,
                        nativeVirtualKeyCode = 34,
                    }));
            }
        }

        private static async Task<bool> WaitUntilAsync(
            Func<Task<bool>> condition,
            int timeoutMilliseconds)
        {
            var deadline = Environment.TickCount64 + timeoutMilliseconds;
            while (Environment.TickCount64 < deadline)
            {
                if (await condition())
                    return true;
                await Task.Delay(40);
            }
            return await condition();
        }
    }
}
