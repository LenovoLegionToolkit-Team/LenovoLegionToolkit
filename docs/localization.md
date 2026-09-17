# Localization Reference

This project uses `.resx` resource files for all user-visible strings.

---

## Resource Files

There are four resource projects:

| Project | File | Used for |
|---------|------|----------|
| `LenovoLegionToolkit.WPF` | `LenovoLegionToolkit.WPF/Resources/Resource.resx` | All UI strings — window titles, labels, buttons, messages |
| `LenovoLegionToolkit.Lib` | `LenovoLegionToolkit.Lib/Resources/Resource.resx` | Enum display names, shared non-UI strings |
| `LenovoLegionToolkit.Lib.Automation` | `LenovoLegionToolkit.Lib.Automation/Resources/Resource.resx` | Automation step, trigger and pipeline display names |
| `LenovoLegionToolkit.Lib.Macro` | `LenovoLegionToolkit.Lib.Macro/Resources/Resource.resx` | Macro source names |

Each has an auto-generated `Resource.Designer.cs` file. **Never edit the `.Designer.cs` files directly** — edit the `.resx` file, then rebuild.

---

## Supported Languages

28 translation files exist alongside the default (English) `.resx`:

`ar`, `bg`, `bs`, `cs`, `de`, `el`, `es`, `fr`, `hi`, `hu`, `id`, `it`, `ja`, `ko`, `lv`, `nl-nl`, `pl`, `pt`, `pt-br`, `ro`, `ru`, `sk`, `tr`, `uk`, `uz-latn-uz`, `vi`, `zh-hans`, `zh-hant`

> **Note:** Only the cultures listed in `LenovoLegionToolkit.WPF/Utils/LocalizationHelper.cs` can be selected in the app. The rest are synced from Crowdin and are not yet exposed in the language picker.

---

## Naming Conventions

Keys follow a hierarchical dot-free naming pattern using underscores:

| Pattern | Example | Used for |
|---------|---------|----------|
| `WindowName_ElementName` | `MainWindow_Title` | Window-scoped labels |
| `WindowName_ElementName_Detail` | `MainWindow_NavigationItem_Dashboard` | More specific window elements |
| `SettingsPage_Feature_Title` | `SettingsPage_Theme_Title` | Settings card titles |
| `SettingsPage_Feature_Message` | `SettingsPage_Theme_Message` | Settings card subtitles/descriptions |
| `SettingsPage_Category_Name` | `SettingsPage_Category_SmartKeys` | Settings section headers |
| `ControlName_Element` | `FanCurveControl_CPU` | Control-specific labels |
| `EnumTypeName_Value` | `PowerModeState_Quiet` | Enum member display names |
| `SingleWord` | `Exit`, `Cancel`, `Yes`, `No` | Global reusable strings |

> **Rule:** Always use the most specific prefix that makes sense. Avoid generic keys that could collide.

---

## Using Resource Strings

### In XAML
```xml
xmlns:resources="clr-namespace:LenovoLegionToolkit.WPF.Resources"

Text="{x:Static resources:Resource.MyKey_Title}"
Title="{x:Static resources:Resource.MyWindow_Title}"
```

### In C# (WPF project)
```csharp
using LenovoLegionToolkit.WPF.Resources;

string text = Resource.MyKey_Title;
string formatted = string.Format(Resource.MyKey_Message, someValue);
```

### In C# (Lib project)
```csharp
using LenovoLegionToolkit.Lib.Resources;

string text = Resource.BestPerformance;
```

---

## Enum Display Names

Enums with localized display names use `[Display]` attributes pointing to the **Lib** resource:

```csharp
using System.ComponentModel.DataAnnotations;
using LenovoLegionToolkit.Lib.Resources;

public enum PowerModeState
{
    [Display(ResourceType = typeof(Resource), Name = "PowerModeState_Quiet")]
    Quiet,
    [Display(ResourceType = typeof(Resource), Name = "PowerModeState_Balance")]
    Balance,
}
```

Rules:
- `ResourceType` always comes **before** `Name`
- `Name` is a plain **string literal** matching the exact key in `Resource.resx`
- The resource key lives in **`LenovoLegionToolkit.Lib/Resources/Resource.resx`**, not the WPF one
- Call `enumValue.GetDisplayName()` (from `EnumExtensions`) to resolve the localized string at runtime

---

## Adding a New Key

1. Open `Resource.resx` in the appropriate project (WPF or Lib)
2. Add a new row with the key name and English value
3. Rebuild — `Resource.Designer.cs` regenerates automatically
4. Add translations to each `Resource.*.resx` file for the relevant languages

> Keys missing from a translation file fall back to the English default automatically.

---

## Common Pitfalls

- **Wrong resource project** — UI strings must go in WPF, enum names in Lib. Mixing them causes compile errors or missing lookups.
- **Unused keys** — If a feature is renamed or removed, clean up the old keys from all `.resx` files. Keys left behind accumulate across all 26 translation files.
- **Hardcoded `[Display(Name = "...")]` without `ResourceType`** — These strings are NOT localized. Always use `[Display(ResourceType = typeof(Resource), Name = "KeyName")]` for enums.
