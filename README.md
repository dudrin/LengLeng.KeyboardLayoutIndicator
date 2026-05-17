# LengLeng Keyboard Layout Indicator

Служба Windows и пользовательский агент для индикации текущей раскладки клавиатуры через выбранный Lock-индикатор клавиатуры.

## Что Делает

- Английская раскладка: выбранный индикатор `Caps Lock`, `Num Lock` или `Scroll Lock` остается в пользовательском состоянии.
- Любая другая раскладка, например русская: выбранный индикатор мигает.
- Если выбран `Caps Lock`, программа запоминает реальное пользовательское состояние Caps Lock и старается не передавать в программы состояние, вызванное миганием.
- Для окон с повышенными правами, например Диспетчера задач, программа не посылает `SendInput`, а показывает короткую надпись с раскладкой по центру активного окна.
- Для русской раскладки надпись показывает `RU`, для английской - `ENG`, для неизвестной неанглийской - `OTHER`.

## Как Сейчас Определяется Раскладка

Рекомендуемый режим по умолчанию - `TrayIndicatorFirst`.

В этом режиме программа сначала читает системный индикатор раскладки в трее Windows (`InputIndicatorButton`) и распознает `ENG`. Это работает для обычных, консольных и многих проблемных приложений, потому что раскладка берется не из конкретного окна, а из системного индикатора.

Если трей показывает не `ENG`, программа дополнительно уточняет конкретный язык через системный input method и HKL активного окна. Поэтому вместо общего `OTHER` для русской раскладки показывается `RU`.

Выделять область `ENG` мышью обычно не нужно. Калибровка осталась как запасной режим на случай, если автоматическое чтение трея ошибается из-за темы, масштаба, шрифта или нестандартного отображения панели задач.

Используйте калибровку только если:

- на английской раскладке диагностика не показывает `Tray indicator is English: True`;
- программа стабильно путает `ENG` и неанглийскую раскладку;
- `Tray indicator layout` в диагностике показывает `unknown`.

Если калибровка была сделана раньше и стала мешать, сбросьте ее через меню значка в трее: `Сбросить область ENG`.

## Почему Есть Служба И Агент

Windows-службы работают в session 0 и не могут напрямую читать активное окно пользователя. Поэтому приложение состоит из двух частей:

- служба `LengLengKeyboardLayoutIndicator` запускается автоматически от `LocalSystem`;
- скрытый агент запускается службой в активной пользовательской сессии, читает раскладку, управляет значком в трее и переключает выбранный Lock-индикатор.

## Оптимизация Нагрузки

Раскладка больше не читается каждые 50 мс. Агент работает по событиям и редкому резервному опросу:

- сразу перечитывает раскладку при смене активного окна;
- перечитывает раскладку при вероятных клавишах переключения языка: `Alt+Shift`, `Ctrl+Shift`, `Win+Space`;
- использует резервный опрос `layoutFallbackPollIntervalMs`, по умолчанию 1000 мс;
- быстрый таймер `layoutPollIntervalMs` используется только для коротких пауз и мигания.

## Окна С Повышенными Правами

Windows блокирует синтетический ввод из обычного процесса в окно с более высоким уровнем прав. Поэтому при активном Диспетчере задач или другом защищенном/elevated-окне программа:

- приостанавливает отправку Lock-клавиши;
- не спамит лог ошибками `SendInput failed`;
- показывает поверх активного окна короткую надпись с текущей раскладкой;
- не показывает эту надпись повторно для того же окна, если раскладка не изменилась.

На secure desktop UAC такая подсказка может не отображаться. Это нормальное ограничение Windows.

## Значок В Трее

Через контекстное меню значка можно:

- выбрать, чем мигать: `Caps Lock`, `Num Lock` или `Scroll Lock`;
- выбрать поведение на английской раскладке: оставить как есть, держать включенным или держать выключенным;
- выбрать стратегию определения раскладки;
- указать область `ENG` мышью, если автоматическое чтение трея ошибается;
- сбросить сохраненную область `ENG`;
- открыть настройки;
- остановить службу и выйти.

## Сборка Установщика

```powershell
.\build-installer.ps1
```

Готовый пакет создается здесь:

```text
artifacts\installer\LengLeng.KeyboardLayoutIndicator
artifacts\LengLeng.KeyboardLayoutIndicator-installer.zip
```

## Установка

Запустите от администратора:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\install.ps1
```

Или запустите:

```text
artifacts\installer\LengLeng.KeyboardLayoutIndicator\Install.cmd
```

Файлы программы устанавливаются в:

```text
C:\Program Files\LengLeng\KeyboardLayoutIndicator
```

Настройки хранятся отдельно и не перезаписываются при переустановке:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\appsettings.json
```

Лог:

```text
C:\ProgramData\LengLeng\KeyboardLayoutIndicator\logs\service.log
```

Установщик также создает:

```text
Start Menu\LengLeng Keyboard Layout Indicator
Public Desktop\LengLeng Keyboard Layout Indicator.lnk
```

Ярлык на рабочем столе запускает службу, если она была остановлена. В меню Пуск есть ярлыки `Start LengLeng`, `Stop service and exit`, `Settings` и `Uninstall LengLeng`.

## Настройки

Рекомендуемые значения находятся в `appsettings.json`:

```json
{
  "settingsSchemaVersion": 9,
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
  "layoutFallbackPollIntervalMs": 1000,
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
  "pauseIndicatorWhileProtectedWindowActive": true,
  "showLayoutOverlayForProtectedWindows": true,
  "layoutOverlayDurationMs": 1400,
  "taskbarPreviewHoverBandPx": 280,
  "taskbarHoverReleasePauseMs": 700,
  "restoreInitialScrollLockStateOnExit": true,
  "logLayoutChanges": true,
  "manualEnglishIndicatorRect": null,
  "manualEnglishIndicatorTemplate": null,
  "manualEnglishIndicatorSearchRadiusPx": 600
}
```

Основные параметры:

- `indicatorKey`: какой индикатор использовать: `CapsLock`, `NumLock` или `ScrollLock`.
- `englishIndicatorState`: `Preserve`, `On` или `Off`. Рекомендуется `Preserve`.
- `englishLanguagePrefixes`: языковые префиксы, которые считаются английскими. Обычно достаточно `["en"]`.
- `indicatorOnBlinkLitMs` и `indicatorOnBlinkDarkMs`: мигание для неанглийской раскладки, когда пользовательское состояние индикатора включено.
- `indicatorOffBlinkLitMs` и `indicatorOffBlinkDarkMs`: мигание для неанглийской раскладки, когда пользовательское состояние индикатора выключено.
- `layoutDetectionStrategy`: стратегия определения раскладки. Рекомендуется `TrayIndicatorFirst`.
- `layoutPollIntervalMs`: внутренний таймер агента для коротких пауз и мигания. Раскладка не читается на каждом тике.
- `layoutFallbackPollIntervalMs`: редкая резервная проверка раскладки.
- `pauseIndicatorWhileModifiersDown`: временно не отправлять Lock-клавишу, пока нажаты `Alt`, `Shift`, `Ctrl` или `Win`.
- `pauseIndicatorWhileTyping`: при реальной печати вернуть Lock-индикатор в пользовательское состояние и временно остановить мигание.
- `pauseIndicatorWhileMouseOverTaskbar`: приостанавливать индикацию, когда курсор находится над панелью задач или областью миниатюр окон.
- `pauseIndicatorWhileProtectedWindowActive`: не отправлять Lock-клавишу в защищенное/elevated-окно.
- `showLayoutOverlayForProtectedWindows`: показывать надпись с раскладкой поверх защищенного окна.
- `layoutOverlayDurationMs`: длительность показа надписи с раскладкой.
- `manualEnglishIndicatorRect` и `manualEnglishIndicatorTemplate`: заполняются только при ручной калибровке `ENG`.
- `manualEnglishIndicatorSearchRadiusPx`: радиус поиска вокруг сохраненной области `ENG`, если используется ручная калибровка.
- `restoreInitialScrollLockStateOnExit`: вернуть пользовательское состояние выбранной Lock-клавиши при остановке агента.
- `logLayoutChanges`: писать смену раскладки в лог.

После изменения файла настроек перезапуск службы обычно не нужен. Агент перечитывает файл автоматически.

## Диагностика

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --diagnostics
```

Проверка одного прохода агента в текущей сессии:

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --agent --once
```

На английской раскладке в диагностике ожидается примерно:

```text
Current layout: en-US (0x0409)
Current layout is English: True
Tray indicator layout: en-US (0x0409)
Tray indicator is English: True
```

На русской раскладке ожидается примерно:

```text
Current layout: ru-RU (0x0419)
Current layout is English: False
Tray indicator layout: tray-non-english (0x0000)
Tray indicator is English: False
```

Если `Tray indicator layout` показывает `unknown` или английский определяется неверно, имеет смысл попробовать ручную калибровку `ENG` через меню трея.

## Удаление

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1
```

После установки программу можно удалить через меню Пуск:

```text
LengLeng Keyboard Layout Indicator\Uninstall LengLeng
```

Удалить программу вместе с настройками:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\uninstall.ps1 -RemoveSettings
```

## Ограничения

Индикатор переключается через стандартный ввод Windows, поэтому фактическое состояние выбранной Lock-клавиши тоже меняется. Для `Caps Lock` агент хранит пользовательское состояние и перехватывает физическое нажатие Caps Lock, но полностью отделить лампу Caps Lock от системного Caps Lock без драйвера нельзя.

Для окон с повышенными правами Windows может блокировать `SendInput`. Поэтому программа использует безопасный режим с надписью поверх окна. Полный обход этого ограничения потребовал бы отдельного `uiAccess`-варианта с подписью и установкой в защищенную папку.

## English Summary

LengLeng Keyboard Layout Indicator is a Windows service plus user-session tray agent that shows the current keyboard layout through a keyboard Lock LED.

- English layout: the selected Lock indicator keeps the user's real state.
- Non-English layout: the selected Lock indicator blinks.
- The default detection strategy reads the Windows tray input indicator first.
- Manual `ENG` calibration is optional and only needed if automatic tray detection fails.
- Elevated/protected foreground windows pause LED output and show a short layout overlay instead.
- Layout refresh is event-driven, with a slow fallback poll.

Build the installer:

```powershell
.\build-installer.ps1
```

Install as administrator:

```powershell
.\artifacts\installer\LengLeng.KeyboardLayoutIndicator\install.ps1
```

Run diagnostics:

```powershell
& "C:\Program Files\LengLeng\KeyboardLayoutIndicator\LengLeng.KeyboardLayoutIndicator.exe" --diagnostics
```
