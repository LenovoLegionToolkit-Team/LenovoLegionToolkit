# Custom Controls & Theme Reference

This document serves as a guide to the custom controls, standard elements, and theme architecture in the Lenovo Legion Toolkit.

---

## 1. Custom Controls Reference

The application defines its own controls on top of `Wpf.Ui` 2.1.0 under two main namespaces:

```xml
xmlns:custom="clr-namespace:LenovoLegionToolkit.WPF.Controls.Custom"
xmlns:controls="clr-namespace:LenovoLegionToolkit.WPF.Controls"
```

### `custom:CardControl`
A content card with an optional icon, header, and right-side content. Wraps `Wpf.Ui.Controls.CardControl` with compact mode support.
*   **Properties:** `Icon` (Symbol name), `Header` (usually a `CardHeaderControl`), `Click`.

```xml
<custom:CardControl Margin="0,0,0,8" Icon="Info24">
    <custom:CardControl.Header>
        <controls:CardHeaderControl Title="Title here" Subtitle="Optional subtitle" />
    </custom:CardControl.Header>
    <wpfui:ToggleSwitch x:Name="_myToggle" />
</custom:CardControl>
```

### `custom:CardAction`
A fully clickable card row with an optional icon and chevron. Wraps `Wpf.Ui.Controls.CardAction`.
*   **Properties:** `Icon`, `Content`, `IsChevronVisible` (default `true`), `Click`.

```xml
<custom:CardAction Margin="0,0,0,4" Icon="Keyboard24" Click="MyAction_Click">
    <controls:CardHeaderControl Title="Action label" />
</custom:CardAction>
```

### `custom:CardExpander`
An expandable card section. Wraps `Wpf.Ui.Controls.CardExpander`.
*   **Properties:** `Header`, `Icon`, `IsExpanded`.

```xml
<custom:CardExpander Icon="Settings24">
    <custom:CardExpander.Header>
        <controls:CardHeaderControl Title="Section title" />
    </custom:CardExpander.Header>
    <!-- expanded content -->
</custom:CardExpander>
```

### `custom:NavigationItem`
A navigation entry used inside `wpfui:NavigationStore`. Wraps `Wpf.Ui.Controls.NavigationItem`.
*   **Properties:** `Content` (label text), `Icon` (Symbol name), `PageType`, `PageTag`.

```xml
<custom:NavigationItem
    Content="{x:Static resources:Resource.MainWindow_NavigationItem_Dashboard}"
    Icon="Home24"
    PageTag="dashboard"
    PageType="{x:Type pages:DashboardPage}" />
```

### `custom:Badge`
A small badge/pill indicator wrapping `Wpf.Ui.Controls.Badge`.

```xml
<custom:Badge Content="NEW" />
```

### `controls:CardHeaderControl`
The standard header block used inside cards.
*   **Properties:**
    *   `Title`: Primary bold label (required).
    *   `Subtitle`: Secondary smaller text below title.
    *   `SubtitleToolTip`: Tooltip shown on subtitle.
    *   `Info`/`Warning`/`Error`/`Success`: Colored status message line with icon.
    *   `Accessory`: UIElement placed on the right column of the header.

```xml
<controls:CardHeaderControl
    Title="{x:Static resources:Resource.MyKey_Title}"
    Subtitle="{x:Static resources:Resource.MyKey_Message}"
    Warning="{Binding MyWarningText}" />
```

### `controls:LoadableControl`
Wraps any content and shows a `ProgressRing` spinner while loading.
*   **Properties:**
    *   `IsLoading`: Shows spinner when `true`.
    *   `IsIndeterminate`: Indeterminate vs. progress ring.
    *   `Progress`: Progress value when not indeterminate.
    *   `ContentVisibilityWhileLoading`: Visibility of content during load.

```xml
<controls:LoadableControl x:Name="_list" IsLoading="True">
    <ItemsControl x:Name="_items" />
</controls:LoadableControl>
```

### `controls:SelectableControl`
A rubber-band (drag-to-select) selection overlay wrapping any content. Used in fan curve and spectrum keyboard editors to drag and select multiple items.

---

## 2. Theme Architecture

The application's look and feel is managed by a theme service layer:

*   **`Theme` Enum** (`Lib\Enums.cs`): Drives active themes (`System`, `Light`, `Dark`).
*   **`ThemeManager`** (`Wpf\Utils\ThemeManager.cs`): Applies the target theme via `Wpf.Ui.Appearance.Theme.Apply` and updates accent colors via `Wpf.Ui.Appearance.Accent.Apply`.
*   **`SystemThemeListener`** (`Lib\Listeners\SystemThemeListener.cs`): Listens for OS theme preference modifications (in the registry) and triggers theme updates to match the system.

---

## 3. Global UI Theme Catalog

To ensure complete coverage when customizing or creating global themes, the theme resource dictionaries target and override the styling brushes for the following elements:

### Containers & Windows
*   `UiWindow` (Base window class, MainWindow, dialogs)
*   `UiPage` / `Page` (Base page element for pages like Dashboard, Settings, Battery)
*   `NavigationStore` (Sidebar navigation container)
*   `ScrollViewer` (Scroll containers used throughout pages and popups)
*   `TitleBar` / `wpfui:TitleBar` (Custom title bars with WinUI controls)
*   `MessageBox` (`Wpf.Ui.Controls.MessageBox` for popups and confirmations)

### Card Controls
*   `CardControl` / `custom:CardControl` (Dashboard widgets)
*   `CardAction` / `custom:CardAction` (Clickable settings rows)
*   `CardExpander` / `custom:CardExpander` (Collapsible configuration panels)
*   `GroupBox` (OSD thresholds, Edit Sensor Group layout)
*   `Expander` (Used in AMD Overclocking settings)

### Navigation & Menus
*   `NavigationItem` / `custom:NavigationItem` (Sidebar tab items)
*   `NavigationHeader` (Sidebar section header labels)
*   `TabControl` & `TabItem` (Inner tab selections, e.g., Spectrum RGB profiles)
*   `ContextMenu` & `MenuItem` (Right-click popups on cards and GPU process list)

### Input & Form Controls
*   `ToggleSwitch` (Standard settings toggles)
*   `ComboBox` & `ComboBoxItem` (Dropdown selection lists)
*   `TextBox` (Text input fields)
*   `NumberBox` (Numeric inputs with spin buttons)
*   `RadioButton` (Visual selection pill elements)
*   `CheckBox` (Checkboxes)
*   `Slider` (Sliders used for overclocking, fan curve thresholds, and background opacity/blur)
*   `Button` (Standard and styled action buttons)

### Lists & Data Displays
*   `ListBox` & `ListBoxItem` (Process selection lists, settings submenus, trigger logs)
*   `ListView` & `ListViewItem` (Process lists for dGPU activity)
*   `ProgressBar` (CPU/GPU/RAM metrics display)
*   `TextBlock` (Heading styles, labels, subtext values)

### Interactive Icons & Custom Helpers
*   `SymbolIcon` (Standard icons for navigation and setting headers)
*   `Snackbar` (System tray & overlay notifications)
*   `ColorPickerControl` & `MultiColorPickerControl` (White/RGB customizable color wheels)
*   `FanCurveControl` & `FanCurveControlV2` (Graph canvases drawing grid lines and node markers)
*   `ToolTip` (Hover description popups)
*   `Hyperlink` / `wpfui:Hyperlink` (Web links)

---

## 4. WPF UI Theme Resource Brushes

To customize the colors of the controls above, resource dictionaries should target and override the following WPF UI brushes and colors:

### Window & Panel Backgrounds
*   `ApplicationBackgroundBrush` / `ApplicationBackgroundColor`: Main window background fill.
*   `SolidBackgroundFillColorBaseBrush`: Primary background fill for container pages.
*   `SolidBackgroundFillColorSecondaryBrush`: Secondary background fill for panels or inputs (e.g. scripting edit area).
*   `SolidBackgroundFillColorTertiaryBrush`: Third-level container background.

### Text & Foregrounds
*   `TextFillColorPrimaryBrush`: Main body text and prominent headers.
*   `TextFillColorSecondaryBrush`: Subtitles, descriptive labels, and secondary texts.
*   `TextFillColorTertiaryBrush`: Inactive placeholder texts and line numbers.
*   `TextFillColorDisabledBrush`: Disabled text elements.
*   `TextOnAccentFillColorPrimaryBrush`: High-contrast text shown on top of accent-colored components (such as primary buttons).

### Card Surfaces
*   `CardBackgroundFillColorDefaultBrush`: Surface background for widgets/cards (`CardControl`, `CardAction`).
*   `CardBackgroundFillColorSecondaryBrush`: Surface background for inner expander/collapsible sections.

### Form Inputs & Buttons (Fill)
*   `ControlFillColorDefaultBrush`: Base background fill for buttons, dropdowns, and checkboxes.
*   `ControlFillColorSecondaryBrush`: Hover state background fill.
*   `ControlFillColorTertiaryBrush`: Pressed state background fill.
*   `ControlFillColorDisabledBrush`: Inactive/disabled background fill.
*   `ControlFillColorInputActiveBrush`: Focused background fill for active text inputs (`TextBox`, `NumberBox`).
*   `SubtleFillColorSecondaryBrush` / `SubtleFillColorTertiaryBrush`: Subtle hover and pressed overlay colors for transparent buttons or menus.

### Borders & Dividers (Strokes)
*   `CardStrokeColorDefaultBrush`: Outer border lines for cards.
*   `ControlStrokeColorDefaultBrush`: Outer border lines for buttons and form elements.
*   `ControlStrokeColorSecondaryBrush`: Hover border lines.
*   `DividerStrokeColorDefaultBrush`: Visual separator line dividers.
*   `ControlElevationBorderBrush`: Shadow/depth border brush for buttons.

### Accent Coloring
*   `SystemAccentColor` / `SystemAccentColorBrush`: The application's core accent color.
*   `SystemAccentColorLight1` / `Light2` / `Light3`: Accent shades used for Light themes.
*   `SystemAccentColorDark1` / `Dark2` / `Dark3`: Accent shades used for Dark themes.
*   `SystemAccentColorPrimary` / `SystemAccentColorSecondary` / `SystemAccentColorTertiary`: Accent variants rewritten by `ThemeManager` through `Wpf.Ui.Appearance.Accent.Apply`. Reference `SystemAccentColorSecondary` from a `SolidColorBrush` when a surface must follow the accent picked in Settings, and give that brush an `Opacity` for a tinted background that matches the weight of the `SystemFillColor*Background` status bars.
*   `TextOnAccentFillColorPrimary`: Text and icon color for a solid accent fill. It flips between black and white depending on accent brightness, so build the brush from the Color (`Color="{DynamicResource TextOnAccentFillColorPrimary}"`); the pre-built `TextOnAccentFillColorPrimaryBrush` resolves its Color with `StaticResource` and therefore does not follow a runtime accent change.

### Status Indicators
*   `SystemFillColorSuccessBrush`: Dynamic brush for success states (e.g., connected/compatible).
*   `SystemFillColorCautionBrush`: Dynamic brush for warnings and caution labels.
*   `SystemFillColorCriticalBrush`: Dynamic brush for critical status levels (e.g., low battery, error alerts).
*   `SystemFillColorNeutralBrush`: Dynamic brush for neutral offline/disabled flags.
