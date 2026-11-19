# WPF UI Icons Reference

This project uses **WPF UI 2.1.0** with **Fluent System Icons v1.1.210**.

## Full Icon List

- **Browse all icons (visual):** https://github.com/microsoft/fluentui-system-icons
- **Full `SymbolRegular` enum (2.1.0):** https://github.com/lepoco/wpfui/blob/2.1.0/src/Wpf.Ui/Common/SymbolRegular.cs
- **Full `SymbolFilled` enum (2.1.0):** https://github.com/lepoco/wpfui/blob/2.1.0/src/Wpf.Ui/Common/SymbolFilled.cs

---

## Naming Convention

```
[IconName][Size]
```

Examples: `Settings24`, `Battery024`, `ArrowClockwise32`, `Desktop16`

**Common sizes in this project:** `20`, `24` (dominant), `16`, `28`, `32`  
**Available sizes in the enum:** `12`, `16`, `20`, `24`, `28`, `32`, `48`

> Not every icon exists at every size. Always verify in the enum before using.

---

## Usage

### XAML — `Icon` property (on `CardControl`, `Button`, etc.)
```xml
<custom:CardControl Icon="Settings24" />
<wpfui:Button Icon="Checkmark24" />
```

### XAML — `SymbolIcon` control
```xml
<wpfui:SymbolIcon Symbol="Warning24" />
<wpfui:SymbolIcon Symbol="Circle16" Filled="True" FontSize="12" />
```

### C# — `SymbolRegular` enum
```csharp
using Wpf.Ui.Common;

card.Icon = SymbolRegular.Keyboard24;
icon.Symbol = SymbolRegular.BatteryCharge24;
```

---

## Icons Used in This Project

All icons below are confirmed to exist in `SymbolRegular` for wpfui 2.1.0.

### General UI
| Icon | Usage |
|------|-------|
| `Checkmark24` | Confirm, select all, apply |
| `CheckmarkCircle24` | Compatibility check flag, success state |
| `Dismiss24` | Close, kill process |
| `Warning24` | Warnings, conflict indicators |
| `Info24` | Info cards, About nav item |
| `ErrorCircle24` | Unsupported device dialog |
| `ChevronDown24` | Expandable sections |
| `Open24` | Open external link |
| `Search24` | Filter/search inputs |
| `Circle16` | Status dot indicator |

### Navigation & Windows
| Icon | Usage |
|------|-------|
| `Home24` | Dashboard nav item |
| `Keyboard24` | Keyboard backlight nav item |
| `BatteryCheckmark24` | Battery nav item |
| `Rocket24` | Actions/Automation nav item |
| `ReceiptPlay24` | Macro nav item |
| `Box24` | Packages nav item |
| `Settings24` | Settings nav item |
| `Heart24` | Donate nav item, Donate page, support indicator |
| `Window24` | OSD overlay style setting |
| `Desktop16` | Display/screen indicator |
| `Desktop24` | GPU working mode flag |
| `LocalLanguage24` | Language selector |

### Hardware & Sensors
| Icon | Usage |
|------|-------|
| `Gauge24` | Status window performance |
| `DeveloperBoard24` | CPU/GPU sensor rows |
| `PointScan24` | CPU overclocking |
| `Games24` | GPU overclocking |
| `ArrowAutofitContent24` | Global offset control |
| `Keyboard24` | Special keys, RGB/spectrum keyboard flags |

### Battery
| Icon | Usage |
|------|-------|
| `Battery024` – `Battery1024` | Battery level indicators (0–100%) |
| `BatteryCharge24` | Charging state |
| `BatterySaver24` | Conservation mode |
| `Battery624` | Static battery icon in status window |

### Actions & Editing
| Icon | Usage |
|------|-------|
| `Edit24` | Rename action in context menu |
| `Delete24` | Delete action in context menu |
| `ArrowClockwise32` | Refresh warranty info |
| `ArrowCounterclockwise24` | Reset |
| `ArrowSync24` | Update indicator, sync |
| `ArrowDownload24` | Disable update checker flag |
| `ArrowImport24` | Import/load |
| `ArrowExport24` | Export/save |

### Visibility
| Icon | Usage |
|------|-------|
| `Eye24` | Reveal serial number, show hidden keys |
| `EyeOff24` | Hide serial number, hide hidden keys |

### Misc / Settings Flags
| Icon | Usage |
|------|-------|
| `Subtract24` | Start minimized flag |
| `Add24` | Increment button |
| `Bug24` | Debug mode flag |
| `Globe24` | Proxy URL flag |
| `Person24` | Proxy username flag |
| `Password24` | Proxy password flag |
| `Certificate24` | Allow all certs flag |
| `Lightbulb24` | Disable Lenovo lighting flag |
| `Color24` | Disable spectrum/RGB keyboard flag |
| `LockClosed24` | OSD lock setting |
| `Timer24` | OSD refresh interval |
| `Target24` | OSD snap threshold |
| `TooltipQuote24` | Disable tray tooltip flag |
| `AppsListDetail24` | Trace logging flag |

---

## Complete List of 183 Referenced Icons

The following is the complete list of unique Fluent System Icons referenced in the application's source code folders:

```
Add24, Airplane24, Alert24, AppGeneric24, Apps24, AppsListDetail24, ArrowAutofitContent24, ArrowCircleDown24, ArrowCircleUp24, ArrowClockwise24, ArrowCounterclockwise24, ArrowDown24, ArrowDownload24, ArrowEject20, ArrowExit20, ArrowExport24, ArrowExportLtr24, ArrowImport24, ArrowLeft48, ArrowMaximize24, ArrowMinimize24, ArrowMove24, ArrowRepeatAll24, ArrowReset24, ArrowRotateClockwise24, ArrowsBidirectional24, ArrowSort24, ArrowStepInRight24, ArrowSync24, ArrowUp24, Battery024, Battery1024, Battery124, Battery224, Battery324, Battery424, Battery524, Battery624, Battery724, Battery824, Battery924, BatteryCharge24, BatteryCheckmark24, BatterySaver24, BorderNone24, Bot24, Box24, BrainCircuit24, BrightnessHigh24, BrightnessHigh48, Broom24, Bug24, CalendarRtl24, Camera24, Certificate24, Checkmark24, Checkmark48, CheckmarkCircle24, ChevronDown20, ChevronDown24, ChevronLeft24, ChevronRight20, ChevronRight24, ChevronRight28, ChevronUp20, ChevronUp24, Circle16, ClipboardPaste24, Clock24, ClockAlarm24, Code24, Color24, ColorFill24, Connected20, Copy24, CopySelect20, Crop24, DarkTheme24, Database24, DataUsage24, Delete24, Desktop16, Desktop24, DesktopPulse24, DeveloperBoard24, DeveloperBoardLightning20, Dismiss12, Dismiss24, Dismiss48, DismissCircle24, DismissSquare24, DocumentBulletList24, Edit24, Empty, ErrorCircle24, Eye24, EyeOff24, FastForward24, Flash24, FlashSettings24, Heart24, History24, Home24, HourglassHalf24, HourglassThreeQuarter24, Image24, Info24, Key24, Keyboard12324, Keyboard24, KeyboardShiftUppercase24, Laptop24, Layer24, LeafOne24, Lightbulb24, LightbulbCircle24, Line24, LineHorizontal320, LineStyle24, Link24, LocalLanguage24, LockClosed24, LockOpen24, Memory16, Mic24, MoreHorizontal24, MusicNote2Play20, NumberSymbol24, Open24, PaintBrush24, Password24, Person24, Pin24, Play24, PlugConnected24, PlugDisconnected24, PointScan24, Power20, Power24, Prohibited24, ProjectionScreen24, PuzzlePiece24, Question24, ReceiptPlay24, Record24, ReOrderDotsVertical24, Resize24, Rocket24, Save24, ScaleFill24, Search24, SelectAllOff24, SelectAllOn24, Settings24, ShieldTask24, Speaker224, SquareMultiple24, Star24, StarEmphasis24, Subtract24, Tablet24, Tag24, Target24, Temperature24, TextFontSize24, TextNumberFormat24, Timer24, TooltipQuote24, TopSpeed24, UsbPlug24, UsbStick24, Wallpaper24, Warning24, WeatherMoon24, Wifi124, WifiOff24, Window16, Window20, Window24, WindowApps24, WindowConsole20, WrenchScrewdriver24, XboxController24
```
