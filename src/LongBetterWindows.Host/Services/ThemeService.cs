using System.Globalization;
using Microsoft.Win32;
using LongBetterWindows.Host.Capabilities;
using LongBetterWindows.Host.Contracts;

namespace LongBetterWindows.Host.Services
{
    public class ThemeService : IThemeService
    {
        private const string ThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightTheme = "AppsUseLightTheme";
        private const string SystemUsesLightTheme = "SystemUsesLightTheme";

        public Task<HostApiResponse<SystemTheme>> GetSystemThemeAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath);
                    if (key == null)
                        return HostApiResponse<SystemTheme>.Failure(ApiErrorCode.NotFound, "无法访问主题注册表");

                    var appsLight = (int?)key.GetValue(AppsUseLightTheme) ?? 1;
                    var theme = appsLight == 1 ? SystemTheme.Light : SystemTheme.Dark;

                    return HostApiResponse<SystemTheme>.Success(theme);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<SystemTheme>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetSystemThemeAsync(SystemTheme theme)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (theme == SystemTheme.Auto)
                        return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, "暂不支持自动主题");

                    using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath, true);
                    if (key == null)
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "无法访问主题注册表");

                    int value = theme == SystemTheme.Light ? 1 : 0;
                    key.SetValue(AppsUseLightTheme, value, RegistryValueKind.DWord);
                    key.SetValue(SystemUsesLightTheme, value, RegistryValueKind.DWord);

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public async Task<HostApiResponse> ToggleThemeAsync()
        {
            var currentResult = await GetSystemThemeAsync();
            if (!currentResult.IsSuccess)
                return HostApiResponse.Failure(currentResult.ErrorCode, currentResult.ErrorMessage);

            var newTheme = currentResult.Data == SystemTheme.Light ? SystemTheme.Dark : SystemTheme.Light;
            return await SetSystemThemeAsync(newTheme);
        }

        public Task<HostApiResponse<string>> GetAccentColorAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                    if (key == null)
                        return HostApiResponse<string>.Failure(ApiErrorCode.NotFound, "无法访问DWM注册表");

                    var colorValue = (int?)key.GetValue("ColorizationColor") ?? 0;
                    var color = $"#{colorValue:X8}";

                    return HostApiResponse<string>.Success(color);
                }
                catch (Exception ex)
                {
                    return HostApiResponse<string>.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }

        public Task<HostApiResponse> SetAccentColorAsync(string color)
        {
            return Task.Run(() =>
            {
                try
                {
                    if (!color.StartsWith("#"))
                        return HostApiResponse.Failure(ApiErrorCode.InvalidArgument, "颜色格式无效");

                    var colorValue = int.Parse(color.TrimStart('#'), NumberStyles.HexNumber);

                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", true);
                    if (key == null)
                        return HostApiResponse.Failure(ApiErrorCode.NotFound, "无法访问DWM注册表");

                    key.SetValue("ColorizationColor", colorValue, RegistryValueKind.DWord);

                    return HostApiResponse.Success();
                }
                catch (Exception ex)
                {
                    return HostApiResponse.Failure(ApiErrorCode.Unknown, ex.Message);
                }
            });
        }
    }
}
