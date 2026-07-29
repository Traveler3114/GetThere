using Microsoft.Maui.Handlers;

// Aliased rather than imported: MAUI's implicit usings already bring in Microsoft.Maui, whose
// Thickness and SolidColorBrush collide with the WinUI types of the same name.
using WinBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using WinThickness = Microsoft.UI.Xaml.Thickness;

namespace GetThere;

/// <summary>
/// Removes the WinUI chrome from controls the design system already draws itself.
/// <para>
/// The design wraps every text field in a <c>Border</c> styled by <c>DsField</c> — a rounded
/// rectangle with a 1px stroke and its own background. On Android and iOS the <c>Entry</c> inside it
/// is effectively borderless, so that reads as one field. On Windows an <c>Entry</c> is a WinUI
/// <c>TextBox</c>, which brings its own square border, its own background and a 2px underline on
/// focus — so the same markup renders a rounded box with a square box inside it, and a line that
/// appears under the text when you click into it. Setting <c>BackgroundColor="Transparent"</c> in
/// <c>DsEntry</c> only addressed the background; the borders come from the control template.
/// </para>
/// <para>
/// The template resolves its brushes and thicknesses through <c>ThemeResource</c> lookups, which
/// check the element's own <c>Resources</c> first — so overriding the keys per instance is what
/// reaches the visual states (pointer-over, focused, disabled) that a plain
/// <c>BorderThickness = 0</c> does not.
/// </para>
/// <para>
/// Applied through the global handler mappers rather than per page, because the mismatch is in every
/// form in the app, not only the two auth pages where it was first noticed.
/// </para>
/// </summary>
internal static class WindowsControlStyling
{
    public static void Apply()
    {
        EntryHandler.Mapper.AppendToMapping(nameof(WindowsControlStyling),
            (handler, _) => StripTextBoxChrome(handler.PlatformView));

        EditorHandler.Mapper.AppendToMapping(nameof(WindowsControlStyling),
            (handler, _) => StripTextBoxChrome(handler.PlatformView));

        // WinUI's CheckBox reserves room for content it does not have here — MAUI's CheckBox is the
        // box alone — which left a wide gap between the box and the "Remember me" label it is
        // supposed to sit beside.
        CheckBoxHandler.Mapper.AppendToMapping(nameof(WindowsControlStyling), (handler, _) =>
        {
            handler.PlatformView.MinWidth = 0;
            handler.PlatformView.Padding = new WinThickness(0);
        });
    }

    private static void StripTextBoxChrome(Microsoft.UI.Xaml.Controls.TextBox textBox)
    {
        var transparent = new WinBrush(Microsoft.UI.Colors.Transparent);
        var noBorder = new WinThickness(0);

        var resources = textBox.Resources;

        // Background across every visual state. Without the state-specific keys the field flashes
        // grey on hover and white on focus, over the card colour DsField painted.
        resources["TextControlBackground"] = transparent;
        resources["TextControlBackgroundPointerOver"] = transparent;
        resources["TextControlBackgroundFocused"] = transparent;
        resources["TextControlBackgroundDisabled"] = transparent;

        resources["TextControlBorderBrush"] = transparent;
        resources["TextControlBorderBrushPointerOver"] = transparent;
        resources["TextControlBorderBrushFocused"] = transparent;
        resources["TextControlBorderBrushDisabled"] = transparent;

        // The focused thickness is the 2px underline; it is a separate key from the resting one.
        resources["TextControlBorderThemeThickness"] = noBorder;
        resources["TextControlBorderThemeThicknessFocused"] = noBorder;

        textBox.BorderThickness = noBorder;
        textBox.Background = transparent;

        // DsField already pads the surrounding Border by 14 horizontally, and its HeightRequest of
        // 50 is what sets the field height — the TextBox's own padding and 32px floor only pushed
        // the text off-centre inside it.
        textBox.Padding = noBorder;
        textBox.MinHeight = 0;
    }
}
