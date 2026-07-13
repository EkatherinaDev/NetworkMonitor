using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace NetworkMonitor;

internal static class AppInstaller
{
    private const string ApplicationFolderName = "NetworkMonitor";
    private const string InstalledExecutableName = "NetworkMonitor.exe";
    private const string DisplayName = "Мониторинг сети";
    private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor";

    public static bool ShouldHandle(string[] args)
    {
        if (args.Any(IsInstallArgument) || args.Any(IsUninstallArgument))
        {
            return true;
        }

        var executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        return executableName.Contains("setup", StringComparison.OrdinalIgnoreCase)
            || executableName.Contains("installer", StringComparison.OrdinalIgnoreCase)
            || executableName.Contains("install", StringComparison.OrdinalIgnoreCase);
    }

    public static void Handle(string[] args)
    {
        if (args.Any(IsUninstallArgument))
        {
            Uninstall();
            return;
        }

        Install();
    }

    private static void Install()
    {
        if (!IsAdministrator())
        {
            RelaunchElevated("--install");
            return;
        }

        try
        {
            var sourcePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show("Не удалось определить путь к установочному файлу.", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var installDirectory = GetInstallDirectory();
            Directory.CreateDirectory(installDirectory);

            var targetPath = Path.Combine(installDirectory, InstalledExecutableName);
            if (!Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }

            CreateStartMenuShortcut(targetPath, installDirectory);
            RegisterUninstaller(targetPath);

            var launch = MessageBox.Show(
                "Установка завершена. Запустить программу сейчас?",
                DisplayName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (launch == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка установки: {ex.Message}", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void Uninstall()
    {
        if (!IsAdministrator())
        {
            RelaunchElevated("--uninstall");
            return;
        }

        try
        {
            DeleteStartMenuShortcut();
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);

            var installDirectory = GetInstallDirectory();
            var currentPath = Environment.ProcessPath ?? string.Empty;
            var runningFromInstallDirectory = Path.GetFullPath(currentPath)
                .StartsWith(Path.GetFullPath(installDirectory), StringComparison.OrdinalIgnoreCase);

            if (runningFromInstallDirectory)
            {
                ScheduleDirectoryRemoval(installDirectory);
            }
            else if (Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
            }

            MessageBox.Show("Программа удалена.", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка удаления: {ex.Message}", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string GetInstallDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ApplicationFolderName);
    }

    private static bool IsInstallArgument(string value)
    {
        return value.Equals("--install", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/install", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUninstallArgument(string value)
    {
        return value.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/uninstall", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated(string argument)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                Arguments = argument,
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запросить права администратора: {ex.Message}", DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void CreateStartMenuShortcut(string targetPath, string workingDirectory)
    {
        var shortcutPath = GetShortcutPath();
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, binder: null, target: shell, args: [shortcutPath]);
            shortcut?.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, binder: null, target: shortcut, args: [targetPath]);
            shortcut?.GetType().InvokeMember("WorkingDirectory", BindingFlags.SetProperty, binder: null, target: shortcut, args: [workingDirectory]);
            shortcut?.GetType().InvokeMember("Description", BindingFlags.SetProperty, binder: null, target: shortcut, args: [DisplayName]);
            shortcut?.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, binder: null, target: shortcut, args: []);
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }

    private static void DeleteStartMenuShortcut()
    {
        var shortcutPath = GetShortcutPath();
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private static string GetShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), $"{DisplayName}.lnk");
    }

    private static void RegisterUninstaller(string targetPath)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", typeof(AppInstaller).Assembly.GetName().Version?.ToString() ?? "1.0.0");
        key.SetValue("Publisher", "Local IT");
        key.SetValue("DisplayIcon", targetPath);
        key.SetValue("InstallLocation", Path.GetDirectoryName(targetPath) ?? string.Empty);
        key.SetValue("UninstallString", $"\"{targetPath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void ScheduleDirectoryRemoval(string directory)
    {
        var arguments = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{directory}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
}
