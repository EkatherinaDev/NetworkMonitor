# Agents Guide / Руководство для агентов

## Русский

### Роль файла

Этот файл описывает техническое устройство проекта для разработчиков и будущих кодовых агентов. Пользовательская документация находится в `README.md`.

### Стек

- Язык: C#.
- UI: Windows Forms.
- Target framework: `net8.0-windows`.
- Сборка: `dotnet publish` через `build.ps1`.
- Пакеты NuGet не используются.

### Основные файлы

- `NetworkMonitor/Form1.cs` - логика формы, обработчики кнопок, запуск сканирования, обновление таблицы.
- `NetworkMonitor/Form1.Designer.cs` - WinForms-разметка интерфейса.
- `NetworkMonitor/NetworkScanner.cs` - ICMP ping, поиск адресов локальной сети, reverse DNS, ARP, TCP-проверка портов, определение серверов.
- `NetworkMonitor/ManualIpStore.cs` - загрузка и сохранение ручных IP.
- `NetworkMonitor/NetworkDevice.cs` - модель строки таблицы.
- `NetworkMonitor/NetworkScanResult.cs` - результат сканирования и прогресс.
- `NetworkMonitor/AppInstaller.cs` - режим само-установки и удаления.
- `NetworkMonitor/Program.cs` - точка входа и выбор режима: установка или обычный запуск.
- `build.ps1` - публикация x64 и x86.

### Важные архитектурные детали

Ping не выполняется через `cmd`, `ping.exe`, batch-файлы или PowerShell. Используется `Ping.SendPingAsync` из .NET.

`arp.exe -a` запускается только для чтения ARP-кэша и показа MAC-адресов. Не используйте ARP как критерий доступности устройства.

`nmblookup` не должен быть внешней зависимостью приложения. Для проверки имени сервера используется встроенный NetBIOS Node Status request по UDP/137, реализованный в `NetworkScanner`. Для серверов `ping` недостаточен: статус online ставится только при ответе имени.

Проверка серверных портов выполняется через `TcpClient`. Список портов и токены имен находятся в `NetworkScanner`.

Автоматическая проверка запускается WinForms-таймером в `Form1`: интервал 10 минут. Таймер должен проверять только известные серверные IP, а не всю подсеть. Полный обход сети должен запускаться только вручную кнопкой `Сканировать всю сеть`.

Сканирование должно оставаться асинхронным. Не блокируйте UI-поток ожиданием ping, DNS, ARP или TCP-портов.

Журнал событий мониторинга находится в `Form1` и отображается через `eventLogListBox`. В журнал нужно писать запуск и завершение проверок, ошибки, найденные серверы и изменения статуса.

### Хранение данных

Ручные IP хранятся здесь:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Формат:

```json
[
  "192.168.1.10"
]
```

Не храните этот файл рядом с EXE в `Program Files`, потому что обычный пользователь может не иметь прав на запись.

Известные серверные IP, которые проверяются автоматически каждые 10 минут, хранятся здесь:

```text
%AppData%\NetworkMonitor\server_ips.json
```

Формат такой же: JSON-массив строк. Файл обслуживается тем же `ManualIpStore`, но с другим именем файла.

### Установка

Опубликованный EXE работает в двух режимах:

- если имя содержит `Setup`, `Install` или `Installer`, запускается установка;
- иначе запускается обычное приложение.

Режим установки копирует EXE в `%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe`, создает ярлык в меню Пуск и запись удаления в `HKLM`.

### Сборка и проверка

Команда проверки проекта:

```powershell
dotnet build .\NetworkMonitor\NetworkMonitor.csproj --configuration Release
```

Команда публикации:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Ожидаемые артефакты:

- `artifacts/NetworkMonitorSetup-x64.exe`
- `artifacts/NetworkMonitorSetup-x86.exe`

### Правила изменений

- Не добавляйте зависимость от внешних сетевых утилит для ping.
- Не добавляйте зависимость от внешней утилиты `nmblookup`; NetBIOS-опрос имени сервера должен оставаться встроенным в код.
- Не запускайте долгие операции в UI-потоке.
- Не возвращайте автоматическое сканирование всей сети по таймеру; таймер должен проверять только серверы.
- Не меняйте формат `manual_ips.json` без обновления `ManualIpStore` и документации.
- Не меняйте формат `server_ips.json` без обновления `ManualIpStore` и документации.
- Если меняете серверные порты или критерии имени, обновите `README.md`.
- После изменения кода выполните `dotnet build`.

## English

### File Purpose

This file documents the project internals for developers and future coding agents. User-facing documentation is in `README.md`.

### Stack

- Language: C#.
- UI: Windows Forms.
- Target framework: `net8.0-windows`.
- Build: `dotnet publish` through `build.ps1`.
- No NuGet packages are used.

### Key Files

- `NetworkMonitor/Form1.cs` - form logic, button handlers, scan orchestration, table rendering.
- `NetworkMonitor/Form1.Designer.cs` - WinForms UI layout.
- `NetworkMonitor/NetworkScanner.cs` - ICMP ping, local target discovery, reverse DNS, ARP, TCP port checks, server detection.
- `NetworkMonitor/ManualIpStore.cs` - manual IP loading and saving.
- `NetworkMonitor/NetworkDevice.cs` - table row model.
- `NetworkMonitor/NetworkScanResult.cs` - scan result and progress records.
- `NetworkMonitor/AppInstaller.cs` - self-install and uninstall mode.
- `NetworkMonitor/Program.cs` - entry point and mode selection.
- `build.ps1` - x64 and x86 publishing script.

### Important Architecture Notes

Ping is not performed through `cmd`, `ping.exe`, batch files, or PowerShell. The application uses .NET `Ping.SendPingAsync`.

`arp.exe -a` is launched only to read the Windows ARP cache and display MAC addresses. Do not use ARP as the source of truth for device availability.

`nmblookup` must not be an external application dependency. Server name verification uses the built-in NetBIOS Node Status request over UDP/137 implemented in `NetworkScanner`. For servers, ping is not enough: online status requires a name response.

Server port checks are performed through `TcpClient`. The port list and hostname tokens are defined in `NetworkScanner`.

Automatic checking is started by a WinForms timer in `Form1`: the interval is 10 minutes. The timer must check only known server IP addresses, not the whole subnet. Full network enumeration must be started only manually by the `Сканировать всю сеть` button.

Scanning must remain asynchronous. Do not block the UI thread while waiting for ping, DNS, ARP, or TCP port checks.

The monitoring event log is owned by `Form1` and displayed through `eventLogListBox`. It should record check starts and finishes, errors, discovered servers, and status changes.

### Data Storage

Manual IP addresses are stored here:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Format:

```json
[
  "192.168.1.10"
]
```

Do not store this file next to the EXE under `Program Files`, because normal users may not have write permission there.

Known server IP addresses used by the 10-minute automatic check are stored here:

```text
%AppData%\NetworkMonitor\server_ips.json
```

The format is the same: a JSON string array. The same `ManualIpStore` class manages it with a different file name.

### Installation

The published EXE has two modes:

- if the filename contains `Setup`, `Install`, or `Installer`, installer mode starts;
- otherwise the regular application starts.

Installer mode copies the EXE to `%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe`, creates a Start Menu shortcut, and registers uninstall information under `HKLM`.

### Build and Verification

Build check:

```powershell
dotnet build .\NetworkMonitor\NetworkMonitor.csproj --configuration Release
```

Publish:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Expected artifacts:

- `artifacts/NetworkMonitorSetup-x64.exe`
- `artifacts/NetworkMonitorSetup-x86.exe`

### Change Rules

- Do not add an external network utility dependency for ping.
- Do not add an external `nmblookup` dependency; NetBIOS server-name probing must remain built into the code.
- Do not run long operations on the UI thread.
- Do not bring back automatic full-network scans on the timer; the timer must check servers only.
- Do not change the `manual_ips.json` format without updating `ManualIpStore` and the docs.
- Do not change the `server_ips.json` format without updating `ManualIpStore` and the docs.
- If server ports or hostname criteria change, update `README.md`.
- Run `dotnet build` after code changes.
