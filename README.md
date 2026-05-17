# LengLeng Keyboard Layout Indicator

Windows service and tray agent for showing the current keyboard layout through a keyboard Lock LED.

- English layout: the selected Lock indicator keeps the user's real state.
- Any other layout, for example Russian: the selected Lock indicator blinks.
- The tray icon lets you choose `Caps Lock`, `Num Lock`, or `Scroll Lock`, calibrate the `ENG` tray indicator area with the mouse, open settings, stop the service, and uninstall the program.

## Why There Is An Agent

Windows services run in session 0 and cannot directly read the active window layout in the interactive user session. The application therefore has two parts:

- the `LengLengKeyboardLayoutIndicator` Windows service starts automatically as `LocalSystem`;
- a hidden user-session agent is launched by the service and reads the active layout, manages the tray icon, and controls the selected Lock indicator.

## Building The Installer

```powershell
.\build-installer.ps1
```

The generated package is created at:

```text
artifacts\installer\LengLeng.KeyboardLayoutIndicator
artifacts\LengLeng.KeyboardLayoutIndicator-installer.zip
```

## Installation

Run as administrator:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\install.ps1
```

Or run `Install.cmd` from the same folder.

Application files are copied to:

```text
C:\Program Files\LengLeng\KeyboardLayoutIndicator
```

The installer also creates:

```text
Start Menu\LengLeng Keyboard Layout Indicator
Public Desktop\LengLeng Keyboard Layout Indicator.lnk
```

The desktop shortcut starts the service if it was stopped. The Start Menu folder contains `Start LengLeng`, `Stop service and exit`, `Settings`, and `Uninstall LengLeng`.

Settings are stored separately and are not overwritten during reinstall:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\appsettings.json
```

Log file:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\logs\service.log
```

## Settings

Recommended defaults are stored in `appsettings.json`:

```json
{
  "settingsSchemaVersion": 7,
  "englishScrollLockState": "Off",
  "indicatorKey": "CapsLock",
  "englishIndicatorState": "Preserve",
  "englishLanguagePrefixes": [ "en" ],
  "blinkIntervalMs": 140,
  "indicatorOnBlinkLitMs": 260,
  "indicatorOnBlinkDarkMs": 80,
  "indicatorOffBlinkLitMs": 120,
  "indicatorOffBlinkDarkMs": 650,
  "layoutPollIntervalMs": 50,
  "serviceSessionPollIntervalMs": 5000,
  "treatUnknownLayoutAsEnglish": true,
  "consoleLayoutStrategy": "ForegroundThread",
  "layoutDetectionStrategy": "TrayIndicatorFirst",
  "trayIndicatorConsoleProcessNames": [ "Far", "Far64" ],
  "pauseIndicatorWhileModifiersDown": true,
  "modifierReleasePauseMs": 250,
  "pauseIndicatorWhileTyping": true,
  "typingPauseMs": 700,
  "pauseIndicatorWhileMouseOverTaskbar": true,
  "taskbarPreviewHoverBandPx": 280,
  "taskbarHoverReleasePauseMs": 700,
  "restoreInitialScrollLockStateOnExit": true,
  "logLayoutChanges": true,
  "manualEnglishIndicatorRect": null,
  "manualEnglishIndicatorTemplate": null,
  "manualEnglishIndicatorSearchRadiusPx": 600
}
```

Main parameters:

- `settingsSchemaVersion`: settings schema version for soft migration of old configuration files.
- `indicatorKey`: which Lock indicator to blink: `CapsLock`, `NumLock`, or `ScrollLock`.
- `englishIndicatorState`: `Preserve`, `On`, or `Off`. `Preserve` is recommended so English layout does not change the real Caps Lock/Num Lock/Scroll Lock state.
- `englishLanguagePrefixes`: language prefixes treated as English. Usually `["en"]` is enough.
- `indicatorOnBlinkLitMs` and `indicatorOnBlinkDarkMs`: non-English blinking pattern when the user's indicator state is on. The lit phase is longer than the dark phase.
- `indicatorOffBlinkLitMs` and `indicatorOffBlinkDarkMs`: non-English blinking pattern when the user's indicator state is off. The dark phase is longer than the lit phase.
- `layoutDetectionStrategy`: `TrayIndicatorFirst` first reads the Windows tray input indicator and recognizes `ENG` by pixels for any active application. If the tray is unavailable, it falls back to the active window HKL. `ForegroundWindow` disables tray reading. `TrayIndicatorForConsole` is a fallback mode for selected console processes only.
- `manualEnglishIndicatorRect` and `manualEnglishIndicatorTemplate`: filled automatically from the tray menu item `Указать область ENG мышью...`.
- `manualEnglishIndicatorSearchRadiusPx`: search radius around the calibrated `ENG` tray indicator. The program searches the tray area because the indicator can move when new tray icons appear.
- `pauseIndicatorWhileTyping`: when real typing is detected, the selected Lock key is immediately restored to the remembered user state and blinking is temporarily paused.
- `pauseIndicatorWhileMouseOverTaskbar`: pauses all indicator output while the mouse is over the taskbar or taskbar thumbnail preview area.
- `taskbarPreviewHoverBandPx`: height of the preview area above the taskbar. Default: `280`.
- `restoreInitialScrollLockStateOnExit`: restores the remembered user state of the selected Lock key when the agent stops.
- `logLayoutChanges`: writes layout changes to the log.

After settings are changed, service restart is not required. The agent reloads the file automatically.

## Diagnostics

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --diagnostics
```

Run one agent iteration in the current session:

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --agent --once
```

## Uninstall

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1
```

After installation, the program can also be removed from:

```text
LengLeng Keyboard Layout Indicator\Uninstall LengLeng
```

To remove settings as well:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1 -RemoveSettings
```

## Limitation

The selected Lock indicator is controlled through standard Windows input, so the real state of that Lock key changes. For `Caps Lock`, the agent keeps a remembered user state: when the physical Caps Lock key is pressed, the normal Windows toggle is intercepted, the remembered state is inverted, blinking is temporarily paused, and Windows receives the corrected state. Fully separating the Caps Lock LED from the system Caps Lock state requires a driver.

The service can be stopped from the tray icon context menu with `Остановить службу и выйти`. Windows may ask for UAC confirmation.

## Русская версия

Служба Windows для индикации текущей раскладки через выбранный Lock-индикатор клавиатуры.

- Английская раскладка: выбранный индикатор оставляется в пользовательском состоянии.
- Любая другая раскладка, например русская: выбранный индикатор мигает.
- Через значок в трее можно выбрать `Caps Lock`, `Num Lock` или `Scroll Lock`, а также указать мышью область значка `ENG` для распознавания раскладки.

## Почему есть агент

Windows-службы работают в session 0 и не видят раскладку активного окна пользователя напрямую. Поэтому приложение состоит из двух частей:

- служба `LengLengKeyboardLayoutIndicator` запускается автоматически от `LocalSystem`;
- скрытый агент запускается службой в активной пользовательской сессии и уже там читает раскладку активного окна.

## Сборка установщика

```powershell
.\build-installer.ps1
```

Готовый пакет появится в:

```text
artifacts\installer\LengLeng.KeyboardLayoutIndicator
artifacts\LengLeng.KeyboardLayoutIndicator-installer.zip
```

## Установка

Запустите от администратора:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\install.ps1
```

Или запустите `Install.cmd` из той же папки.

Установка копирует файлы в:

```text
C:\Program Files\LengLeng\KeyboardLayoutIndicator
```

Также создаются:

```text
Меню Пуск\LengLeng Keyboard Layout Indicator
Общий рабочий стол\LengLeng Keyboard Layout Indicator.lnk
```

Ярлык на рабочем столе запускает службу, если она была остановлена. В папке меню Пуск есть ярлыки `Start LengLeng`, `Stop service and exit`, `Settings` и `Uninstall LengLeng`.

Настройки хранятся отдельно и при переустановке не перезаписываются:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\appsettings.json
```

Лог:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\logs\service.log
```

## Настройки

Рекомендованные значения уже записаны в `appsettings.json`:

```json
{
  "settingsSchemaVersion": 7,
  "englishScrollLockState": "Off",
  "indicatorKey": "CapsLock",
  "englishIndicatorState": "Preserve",
  "englishLanguagePrefixes": [ "en" ],
  "blinkIntervalMs": 140,
  "indicatorOnBlinkLitMs": 260,
  "indicatorOnBlinkDarkMs": 80,
  "indicatorOffBlinkLitMs": 120,
  "indicatorOffBlinkDarkMs": 650,
  "layoutPollIntervalMs": 50,
  "serviceSessionPollIntervalMs": 5000,
  "treatUnknownLayoutAsEnglish": true,
  "consoleLayoutStrategy": "ForegroundThread",
  "layoutDetectionStrategy": "TrayIndicatorFirst",
  "trayIndicatorConsoleProcessNames": [ "Far", "Far64" ],
  "pauseIndicatorWhileModifiersDown": true,
  "modifierReleasePauseMs": 250,
  "pauseIndicatorWhileTyping": true,
  "typingPauseMs": 700,
  "pauseIndicatorWhileMouseOverTaskbar": true,
  "taskbarPreviewHoverBandPx": 280,
  "taskbarHoverReleasePauseMs": 700,
  "restoreInitialScrollLockStateOnExit": true,
  "logLayoutChanges": true,
  "manualEnglishIndicatorRect": null,
  "manualEnglishIndicatorTemplate": null,
  "manualEnglishIndicatorSearchRadiusPx": 600
}
```

Параметры:

- `settingsSchemaVersion`: версия схемы настроек, используется для мягкой миграции старых конфигов.
- `englishScrollLockState`: старый параметр для совместимости.
- `indicatorKey`: чем мигать: `CapsLock`, `NumLock` или `ScrollLock`.
- `englishIndicatorState`: `Preserve`, `On` или `Off`. Рекомендовано `Preserve`, чтобы на английской раскладке не менять реальное состояние Caps Lock/Num Lock/Scroll Lock.
- `englishLanguagePrefixes`: какие языки считать английскими. Обычно достаточно `["en"]`.
- `blinkIntervalMs`: старый общий интервал мигания для совместимости.
- `indicatorOnBlinkLitMs` и `indicatorOnBlinkDarkMs`: мигание для неанглийской раскладки, когда пользовательское состояние индикатора включено. Свет горит дольше, чем пауза.
- `indicatorOffBlinkLitMs` и `indicatorOffBlinkDarkMs`: мигание для неанглийской раскладки, когда пользовательское состояние индикатора выключено. Пауза дольше, чем свет.
- `layoutPollIntervalMs`: частота проверки раскладки. Рекомендовано `50`.
- `serviceSessionPollIntervalMs`: как часто служба проверяет активные пользовательские сессии.
- `treatUnknownLayoutAsEnglish`: если активное окно недоступно, не мигать.
- `consoleLayoutStrategy`: стратегия для консольных окон. `ForegroundThread` не ломает английскую раскладку в Far. `PreferNonEnglishProcessThread` может помочь с русской раскладкой в Far, но у Far иногда остаются старые русские HKL в фоновых потоках, поэтому английская раскладка может ошибочно мигать.
- `layoutDetectionStrategy`: `TrayIndicatorFirst` сначала читает область индикатора раскладки в трее и распознаёт `ENG` по пикселям для любого активного приложения. Если трэй недоступен, используется HKL активного окна. `ForegroundWindow` полностью отключает чтение трея. `TrayIndicatorForConsole` оставлен как запасной режим только для выбранных консольных процессов.
- `trayIndicatorConsoleProcessNames`: имена консольных процессов для режима `TrayIndicatorForConsole`. В рекомендованном режиме `TrayIndicatorFirst` этот список игнорируется.
- `manualEnglishIndicatorRect` и `manualEnglishIndicatorTemplate`: заполняются автоматически из меню трея `Указать область ENG мышью...`.
- `manualEnglishIndicatorSearchRadiusPx`: радиус поиска вокруг откалиброванного значка `ENG`. Программа ищет шаблон по области трея, потому что значок раскладки может сдвигаться при появлении новых значков.
- `pauseIndicatorWhileModifiersDown`: приостанавливать отправку выбранной Lock-клавиши, пока нажаты `Alt`, `Shift`, `Ctrl` или `Win`.
- `modifierReleasePauseMs`: пауза после отпускания модификаторов, чтобы не мешать переключению раскладки.
- `pauseIndicatorWhileTyping`: при реальной печати сразу возвращать выбранный Lock-индикатор в запомненное пользовательское состояние и временно останавливать мигание.
- `typingPauseMs`: сколько миллисекунд держать паузу мигания после последнего нажатия клавиши.
- `pauseIndicatorWhileMouseOverTaskbar`: приостанавливать индикацию, когда курсор находится над панелью задач или областью миниатюр открытых окон.
- `taskbarPreviewHoverBandPx`: высота зоны над панелью задач, где ожидаются миниатюры окон. Рекомендовано `280`.
- `taskbarHoverReleasePauseMs`: дополнительная пауза после ухода курсора из зоны панели задач/миниатюр.
- `restoreInitialScrollLockStateOnExit`: вернуть запомненное пользовательское состояние выбранной Lock-клавиши при остановке агента.
- `logLayoutChanges`: писать смену раскладки в лог.

После изменения настроек перезапуск службы не нужен: агент перечитывает файл автоматически.

## Диагностика

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --diagnostics
```

Для проверки агента в текущей сессии без долгого запуска:

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --agent --once
```

## Удаление

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1
```

После установки удалить программу можно также через меню Пуск:

```text
LengLeng Keyboard Layout Indicator\Uninstall LengLeng
```

Чтобы удалить и настройки:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1 -RemoveSettings
```

## Ограничение

Индикатор переключается через стандартный ввод Windows, поэтому фактическое состояние выбранной Lock-клавиши тоже меняется. Для `Caps Lock` агент хранит пользовательское состояние: при реальном нажатии Caps Lock штатное переключение перехватывается, запомненное состояние инвертируется, мигание временно останавливается, а Windows получает уже исправленное состояние. Полностью отделить лампу Caps Lock от системного Caps Lock без драйвера нельзя.

Службу можно остановить из контекстного меню значка в трее пунктом `Остановить службу и выйти`. Для этой операции Windows может запросить подтверждение UAC.
