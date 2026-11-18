using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Vocabulary;

internal sealed class CardBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? controller;
    private SystemBackdropConfiguration? configuration;
    private FrameworkElement? root;
    private int density = 50;

    internal int Density
    {
        set
        {
            density = Math.Clamp(value, 0, 100);
            ApplyAppearance();
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        root = (FrameworkElement)xamlRoot.Content;
        if (!DesktopAcrylicController.IsSupported()) return;
        configuration = new SystemBackdropConfiguration { IsInputActive = true };
        controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        controller.SetSystemBackdropConfiguration(configuration);
        controller.AddSystemBackdropTarget(target);
        root.ActualThemeChanged += ThemeChanged;
        ApplyAppearance();
    }

    private void ThemeChanged(FrameworkElement sender, object args) => ApplyAppearance();

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // This backdrop owns its configuration and controller throughout Explorer attachment.
        ApplyAppearance();
    }

    internal void ApplyAppearance()
    {
        if (controller is null || configuration is null || root is null) return;
        bool dark = root.ActualTheme == ElementTheme.Dark;
        configuration.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        configuration.IsInputActive = true;
        controller.TintColor = dark ? Colors.Black : Colors.White;
        bool highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        controller.FallbackColor = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : dark ? Windows.UI.Color.FromArgb(255, 37, 39, 40) : Windows.UI.Color.FromArgb(255, 245, 247, 248);
        controller.TintOpacity = (float)(0.08 + density * 0.0045);
        controller.LuminosityOpacity = (float)(0.25 + density * 0.005);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (root is not null) root.ActualThemeChanged -= ThemeChanged;
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
        root = null;
        configuration = null;
        base.OnTargetDisconnected(target);
    }
}
