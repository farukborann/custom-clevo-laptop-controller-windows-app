using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BSCustomClevoController.Utility
{
    public static class StartupManager
    {
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "CustomClevoController";
        private const string TaskName = "CustomClevoControllerStartup";
        private const string SchtasksExe = "schtasks.exe";

        public static string GetExecutablePath()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    string fileName = process.MainModule.FileName;
                    if (!string.IsNullOrEmpty(fileName) && File.Exists(fileName))
                    {
                        return Path.GetFullPath(fileName);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.GetExecutablePath.MainModule", ex);
            }

            try
            {
                string path = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.GetExecutablePath.AssemblyLocation", ex);
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string winUiPath = Path.Combine(baseDir, "BSCustomClevoController.App.exe");
            if (File.Exists(winUiPath))
            {
                return winUiPath;
            }

            return Path.Combine(baseDir, "BSCustomClevoController.exe");
        }

        public static bool IsRunAtStartupEnabled()
        {
            return IsTaskScheduled()
                || HasRunRegistryValue(Registry.CurrentUser)
                || HasRunRegistryValue(Registry.LocalMachine);
        }

        public static bool SetRunAtStartup(bool enable)
        {
            return enable ? EnableStartup() : DisableStartup();
        }

        private static bool EnableStartup()
        {
            if (CreateStartupTask() || IsTaskScheduled())
            {
                // Task Scheduler runs with HighestAvailable privileges on logon without UAC prompts.
                // Clean up redundant Run registry keys to avoid duplicate startup launches and UAC prompts.
                RemoveRunRegistryKeys();
                return true;
            }

            // Fallback only if Task Scheduler task creation failed (e.g. policy restriction)
            SetCurrentUserRunValue(GetExecutablePath());
            return IsRunAtStartupEnabled();
        }

        private static bool DisableStartup()
        {
            RemoveStartupTask();
            RemoveRunRegistryKeys();
            return !IsRunAtStartupEnabled();
        }

        public static void RemoveRunRegistryKeys()
        {
            DeleteRunRegistryValueIfPresent(Registry.CurrentUser, "StartupManager.RemoveRunRegistryKeyHKCU");
            DeleteRunRegistryValueIfPresent(Registry.LocalMachine, "StartupManager.RemoveRunRegistryKeyHKLM");
        }

        public static void EnsureConfiguredCorrectly()
        {
            try
            {
                bool taskScheduled = IsTaskScheduled();
                bool hasRunKey = HasRunRegistryValue(Registry.CurrentUser) || HasRunRegistryValue(Registry.LocalMachine);

                if (!taskScheduled && !hasRunKey)
                {
                    return;
                }

                if (!taskScheduled)
                {
                    taskScheduled = CreateStartupTask() || IsTaskScheduled();
                }

                if (taskScheduled && hasRunKey)
                {
                    // Clean up duplicate Run registry key when Task Scheduler is active
                    RemoveRunRegistryKeys();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.EnsureConfiguredCorrectly", ex);
            }
        }

        private static bool HasRunRegistryValue(RegistryKey hive)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(RunRegistryKey, false))
                {
                    return key != null && key.GetValue(AppName) != null;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.HasRunRegistryValue", ex);
                return false;
            }
        }

        private static void SetCurrentUserRunValue(string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        key.SetValue(AppName, "\"" + exePath + "\" /minimized");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.SetRunRegistryKey", ex);
            }
        }

        private static void DeleteRunRegistryValueIfPresent(RegistryKey hive, string logContext)
        {
            if (!HasRunRegistryValue(hive))
            {
                return;
            }

            try
            {
                using (RegistryKey key = hive.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null && key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log(logContext, ex);
            }
        }

        private static string BuildTaskXml(string exePath)
        {
            string exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
            return string.Format(
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo>\r\n" +
                "    <Author>Control Center</Author>\r\n" +
                "    <Description>Starts Control Center with elevated privileges at user logon.</Description>\r\n" +
                "    <URI>\\{0}</URI>\r\n" +
                "  </RegistrationInfo>\r\n" +
                "  <Principals>\r\n" +
                "    <Principal id=\"Author\">\r\n" +
                "      <LogonType>InteractiveToken</LogonType>\r\n" +
                "      <RunLevel>HighestAvailable</RunLevel>\r\n" +
                "    </Principal>\r\n" +
                "  </Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>4</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Triggers>\r\n" +
                "    <LogonTrigger>\r\n" +
                "      <Enabled>true</Enabled>\r\n" +
                "    </LogonTrigger>\r\n" +
                "  </Triggers>\r\n" +
                "  <Actions Context=\"Author\">\r\n" +
                "    <Exec>\r\n" +
                "      <Command>{1}</Command>\r\n" +
                "      <Arguments>/minimized</Arguments>\r\n" +
                "      <WorkingDirectory>{2}</WorkingDirectory>\r\n" +
                "    </Exec>\r\n" +
                "  </Actions>\r\n" +
                "</Task>",
                TaskName,
                System.Security.SecurityElement.Escape(exePath),
                System.Security.SecurityElement.Escape(exeDir));
        }

        private static dynamic ConnectRootTaskFolder()
        {
            Type serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType == null)
            {
                return null;
            }

            dynamic service = Activator.CreateInstance(serviceType);
            service.Connect();
            return service.GetFolder(@"\");
        }

        private static bool IsTaskNotFoundException(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return true;
            }

            if (ex is COMException comEx)
            {
                const int ErrorFileNotFound = unchecked((int)0x80070002);
                const int ErrorPathNotFound = unchecked((int)0x80070003);
                if (comEx.HResult == ErrorFileNotFound || comEx.HResult == ErrorPathNotFound)
                {
                    return true;
                }
            }

            return ex.InnerException != null && IsTaskNotFoundException(ex.InnerException);
        }

        private static bool RegisterTaskViaCom(string xml)
        {
            try
            {
                dynamic rootFolder = ConnectRootTaskFolder();
                if (rootFolder == null)
                {
                    return false;
                }

                rootFolder.RegisterTask(TaskName, xml, 6, null, null, 3, null);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.RegisterTaskViaCom", ex);
                return false;
            }
        }

        private static bool RegisterTaskViaXmlCli(string xml)
        {
            string tempFile = null;
            try
            {
                tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
                using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(fs, System.Text.Encoding.Unicode))
                {
                    writer.Write(xml);
                }

                return ProcessRunner.Run(
                    ProcessRunner.ResolveSystemTool(SchtasksExe),
                    string.Format("/create /f /tn \"{0}\" /xml \"{1}\"", TaskName, tempFile),
                    5000,
                    out _) == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.RegisterTaskViaXmlCli", ex);
                return false;
            }
            finally
            {
                if (tempFile != null && File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch (Exception ex) { AppLogger.Log("StartupManager.DeleteTempXml", ex); }
                }
            }
        }

        private static bool RegisterTaskViaCli(string exePath)
        {
            try
            {
                string args = string.Format("/create /f /tn \"{0}\" /sc onlogon /rl highest /tr \"\\\"{1}\\\" /minimized\"", TaskName, exePath);
                return ProcessRunner.Run(ProcessRunner.ResolveSystemTool(SchtasksExe), args, 5000, out _) == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.RegisterTaskViaCli", ex);
                return false;
            }
        }

        private static bool CreateStartupTask()
        {
            string exePath = GetExecutablePath();
            string xml = BuildTaskXml(exePath);

            if (RegisterTaskViaCom(xml))
            {
                return true;
            }

            if (RegisterTaskViaXmlCli(xml))
            {
                return true;
            }

            return RegisterTaskViaCli(exePath);
        }

        private static void RemoveStartupTask()
        {
            try
            {
                dynamic rootFolder = ConnectRootTaskFolder();
                if (rootFolder != null)
                {
                    rootFolder.DeleteTask(TaskName, 0);
                    return;
                }
            }
            catch (Exception ex) when (IsTaskNotFoundException(ex))
            {
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.RemoveStartupTaskCom", ex);
            }

            try
            {
                ProcessRunner.Run(ProcessRunner.ResolveSystemTool(SchtasksExe), string.Format("/delete /f /tn \"{0}\"", TaskName), 5000, out _);
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.RemoveStartupTaskCli", ex);
            }
        }

        private static bool IsTaskScheduled()
        {
            try
            {
                dynamic rootFolder = ConnectRootTaskFolder();
                if (rootFolder != null)
                {
                    dynamic task = rootFolder.GetTask(TaskName);
                    return task != null && (bool)task.Enabled;
                }
            }
            catch (Exception ex) when (IsTaskNotFoundException(ex))
            {
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.IsTaskScheduledCom", ex);
            }

            try
            {
                return ProcessRunner.Run(ProcessRunner.ResolveSystemTool(SchtasksExe), string.Format("/query /tn \"{0}\"", TaskName), 5000, out _) == 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log("StartupManager.IsTaskScheduledCli", ex);
                return false;
            }
        }
    }
}
