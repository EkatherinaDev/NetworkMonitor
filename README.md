# Network Monitor / Мониторинг сети

## Русский

### Назначение

Network Monitor - Windows-приложение для контроля доступности устройств в локальной IPv4-сети. Программа автоматически сканирует сеть каждые 10 минут, показывает найденные устройства в таблице, выделяет вероятные серверы и позволяет вручную добавить или проверить конкретный IP-адрес.

### Как работает проверка IP

Приложение не запускает `cmd.exe`, `ping.exe`, `.bat` или `.ps1` для проверки доступности IP. Ping выполняется напрямую из C# через `System.Net.NetworkInformation.Ping.SendPingAsync`.

Для каждого IP используется ICMP echo request с таймаутом 800 мс. Если ответ успешный, устройство считается доступным. Если ответа нет, IP помечается как недоступный.

### Как выбираются адреса для сканирования

При сканировании приложение читает активные сетевые интерфейсы Windows через `NetworkInterface.GetAllNetworkInterfaces()`. Для каждого IPv4-адреса и маски вычисляется подсеть, после чего формируется список IP-адресов для проверки.

Если подсеть слишком большая, приложение ограничивает автоматический перебор ближайшим `/24` диапазоном, чтобы не запускать слишком тяжелое сканирование. Адреса, добавленные вручную, всегда добавляются в список проверки дополнительно.

### Автоматическое и ручное сканирование

Автоматическое сканирование запускается таймером WinForms каждые 10 минут. Первый запуск выполняется сразу после открытия окна.

Кнопка `Сканировать сеть` запускает такое же сканирование вручную. Во время проверки интерфейс не блокируется: ping выполняется параллельно, до 128 IP одновременно.

Двойной щелчок по строке таблицы проверяет только выбранный IP. Кнопка `Проверить IP` проверяет адрес из поля ввода. Кнопка `Добавить` сохраняет IP в список ручных адресов и сразу проверяет его.

### Как определяется имя устройства

Для доступных IP программа делает reverse DNS lookup через `Dns.GetHostEntryAsync`. Если DNS возвращает полное имя, в таблице показывается короткая часть до первой точки. Если имя определить не удалось, выводится `Неизвестное устройство`.

### Как определяется MAC-адрес

MAC-адрес берется из ARP-кэша Windows. Для этого приложение запускает системную команду:

```powershell
arp.exe -a
```

Затем вывод разбирается регулярным выражением. Это не используется для ping-проверки; `arp.exe` нужен только для отображения MAC-адреса уже известных соседних устройств.

### Как определяются серверы

Сервер определяется эвристически. Устройство считается вероятным сервером, если выполняется одно из условий:

- имя содержит признаки сервера: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- открыт один из серверных портов: `22`, `25`, `53`, `80`, `389`, `443`, `636`, `1433`, `1521`, `3306`, `3389`, `5432`, `8080`, `8443`.

Проверка портов выполняется через `TcpClient`, без запуска внешних утилит. Таймаут подключения к одному порту - 300 мс.

Вероятные серверы сортируются вверху таблицы и выделяются жирным шрифтом.

### Где хранятся IP-адреса

IP-адреса, добавленные вручную, хранятся в JSON-файле профиля пользователя:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

Файл содержит обычный массив строк:

```json
[
  "192.168.1.10",
  "192.168.1.20"
]
```

При сохранении адреса нормализуются, дубликаты удаляются, список сортируется по IP.

### Установка и удаление

Готовые файлы находятся в папке `artifacts`:

- `NetworkMonitorSetup-x64.exe`
- `NetworkMonitorSetup-x86.exe`

Если EXE-файл содержит в имени `Setup`, `Install` или `Installer`, приложение запускается в режиме установки. Оно запрашивает права администратора, копирует себя в:

```text
%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe
```

Также создается ярлык в общем меню Пуск и запись удаления в реестре Windows:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor
```

Исходный код пользователям не передается. Пользователь получает один самодостаточный EXE-файл нужной разрядности.

### Сборка

Требуется .NET SDK 8 или новее с поддержкой Windows Desktop.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Скрипт собирает две self-contained single-file версии:

- `win-x64`
- `win-x86`

### Запуск для разработки

```powershell
dotnet run --project .\NetworkMonitor\NetworkMonitor.csproj
```

## English

### Purpose

Network Monitor is a Windows desktop application for checking device availability in a local IPv4 network. It scans the network automatically every 10 minutes, displays discovered devices in a table, moves probable servers to the top, and allows users to add or check specific IP addresses manually.

### How IP Checks Work

The application does not run `cmd.exe`, `ping.exe`, `.bat`, or `.ps1` files to check IP availability. Ping is executed directly from C# by using `System.Net.NetworkInformation.Ping.SendPingAsync`.

Each IP receives an ICMP echo request with an 800 ms timeout. A successful reply marks the device as online. If no reply is received, the IP is marked as unavailable.

### How Scan Targets Are Selected

During a scan, the application reads active Windows network interfaces by using `NetworkInterface.GetAllNetworkInterfaces()`. For each IPv4 address and subnet mask, it calculates the subnet and builds a list of IP addresses to check.

If a subnet is too large, automatic enumeration is limited to the nearest `/24` range to avoid an overly heavy scan. Manually added IP addresses are always included.

### Automatic and Manual Scanning

Automatic scanning is started by a WinForms timer every 10 minutes. The first scan starts when the main window is shown.

The `Сканировать сеть` button starts the same scan manually. The UI remains responsive because ping checks run in parallel, up to 128 IP addresses at a time.

Double-clicking a table row checks only the selected IP. The `Проверить IP` button checks the address from the input field. The `Добавить` button saves the IP to the manual list and checks it immediately.

### Hostname Detection

For online IP addresses, the application performs a reverse DNS lookup by using `Dns.GetHostEntryAsync`. If DNS returns a full hostname, only the short name before the first dot is displayed. If no name can be resolved, the UI shows `Неизвестное устройство`.

### MAC Address Detection

MAC addresses are read from the Windows ARP cache. For this part only, the application starts the system command:

```powershell
arp.exe -a
```

The output is parsed with a regular expression. This is not used for ping checks; `arp.exe` is used only to display MAC addresses for known neighboring devices.

### Server Detection

Server detection is heuristic. A device is treated as a probable server when at least one condition is true:

- the hostname contains server-like tokens: `server`, `srv`, `dc`, `sql`, `db`, `1c`, `ksc`, `mail`, `exchange`, `nas`, `storage`, `backup`, `terminal`, `rdp`, `web`, `app`;
- one of the known server ports is open: `22`, `25`, `53`, `80`, `389`, `443`, `636`, `1433`, `1521`, `3306`, `3389`, `5432`, `8080`, `8443`.

Port checks are performed through `TcpClient`, without external tools. The connection timeout for one port is 300 ms.

Probable servers are sorted to the top of the table and displayed in bold.

### IP Storage

Manually added IP addresses are stored in the current user's profile:

```text
%AppData%\NetworkMonitor\manual_ips.json
```

The file contains a plain JSON string array:

```json
[
  "192.168.1.10",
  "192.168.1.20"
]
```

Before saving, addresses are normalized, duplicates are removed, and the list is sorted by IP.

### Installation and Uninstallation

Built executables are placed in the `artifacts` directory:

- `NetworkMonitorSetup-x64.exe`
- `NetworkMonitorSetup-x86.exe`

If the EXE filename contains `Setup`, `Install`, or `Installer`, the application starts in installer mode. It requests administrator rights and copies itself to:

```text
%ProgramFiles%\NetworkMonitor\NetworkMonitor.exe
```

It also creates a Start Menu shortcut and an uninstall registry entry:

```text
HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor
```

End users receive one self-contained EXE for the required architecture. Source code is not distributed to users.

### Build

.NET SDK 8 or newer with Windows Desktop support is required.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script builds two self-contained single-file versions:

- `win-x64`
- `win-x86`

### Development Run

```powershell
dotnet run --project .\NetworkMonitor\NetworkMonitor.csproj
```
