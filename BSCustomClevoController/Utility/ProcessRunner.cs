using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BSCustomClevoController.Utility
{
    public static class ProcessRunner
    {
        public static string ResolveSystemTool(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(fileName) && File.Exists(fileName))
                {
                    return Path.GetFullPath(fileName);
                }

                string sysDir = Environment.SystemDirectory;
                if (string.Equals(fileName, "powershell.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string psPath = Path.Combine(sysDir, @"WindowsPowerShell\v1.0\powershell.exe");
                    if (File.Exists(psPath))
                    {
                        return psPath;
                    }
                }

                string toolPath = Path.Combine(sysDir, fileName);
                if (File.Exists(toolPath))
                {
                    return toolPath;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log("ProcessRunner.ResolveSystemTool:" + fileName, ex);
            }

            return null;
        }

        public static int Run(string fileName, string arguments, int timeoutMs, out string combinedOutput)
        {
            string resolvedTool = ResolveSystemTool(fileName);
            if (string.IsNullOrEmpty(resolvedTool))
            {
                combinedOutput = string.Empty;
                return -1;
            }

            StringBuilder output = new StringBuilder();
            object outputLock = new object();

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = resolvedTool,
                Arguments = arguments,
                WorkingDirectory = Environment.SystemDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (Process process = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler appendLine = delegate (object sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null)
                    {
                        return;
                    }
                    lock (outputLock)
                    {
                        output.AppendLine(e.Data);
                    }
                };

                process.OutputDataReceived += appendLine;
                process.ErrorDataReceived += appendLine;
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(1000);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log("ProcessRunner.Kill:" + fileName, ex);
                    }
                    lock (outputLock)
                    {
                        combinedOutput = output.ToString();
                    }
                    return -1;
                }

                // Flush async output callbacks before reading final buffer
                process.WaitForExit();
                lock (outputLock)
                {
                    combinedOutput = output.ToString();
                }
                return process.ExitCode;
            }
        }
    }
}

