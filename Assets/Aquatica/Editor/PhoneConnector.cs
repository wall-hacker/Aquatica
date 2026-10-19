#if UNITY_EDITOR_WIN
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Aquatica.EditorTools
{
    /// <summary>
    /// Connects the phone over Wireless debugging by running Tools/Connect-Phone.ps1, which remembers the
    /// phone's IP and last port per Wi-Fi network. Runs once when the editor opens, before every Android
    /// Build And Run, and from the Aquatica/Connect Phone menu. Uses the adb from Unity's configured SDK so
    /// Unity and the script never restart each other's adb server.
    /// </summary>
    [InitializeOnLoad]
    public class PhoneConnector : IPreprocessBuildWithReport
    {
        const string ScriptPath = "Tools/Connect-Phone.ps1"; // relative to the project root
        const int BuildTimeoutMs = 60000;

        public int callbackOrder => -1000;

        static PhoneConnector()
        {
            // Static constructors rerun on every domain reload; only connect once per editor session.
            if (SessionState.GetBool("Aquatica.PhoneConnector.Ran", false))
                return;
            SessionState.SetBool("Aquatica.PhoneConnector.Ran", true);
            EditorApplication.delayCall += () => ConnectInBackground(quiet: true);
        }

        [MenuItem("Aquatica/Connect Phone")]
        static void ConnectFromMenu() => ConnectInBackground(quiet: false);

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android ||
                (report.summary.options & BuildOptions.AutoRunPlayer) == 0)
                return;

            EditorUtility.DisplayProgressBar("Connect Phone", "Connecting to the phone over Wireless debugging...", 0.5f);
            try
            {
                var (exitCode, output) = Run(AdbPath(), BuildTimeoutMs);
                Report(exitCode, output, quiet: false);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void ConnectInBackground(bool quiet)
        {
            var adb = AdbPath(); // read Unity settings on the main thread
            var mainThread = TaskScheduler.FromCurrentSynchronizationContext();
            Task.Run(() => Run(adb, BuildTimeoutMs))
                .ContinueWith(t => Report(t.Result.exitCode, t.Result.output, quiet), mainThread);
        }

        // Main thread only: logs the result and points Unity's Android device address at the phone's current
        // IP:port, replacing whatever was typed into Build Profiles > Android > Run Device before.
        static void Report(int exitCode, string output, bool quiet)
        {
            if (exitCode != 0)
            {
                if (!quiet)
                    Debug.LogWarning($"[Connect Phone] Not connected (exit {exitCode}).\n{output}");
                return;
            }

            var endpoint = Regex.Match(output, @"((?:\d{1,3}\.){3}\d{1,3}:\d+)\s+device");
            if (endpoint.Success)
                SetDeviceSocketAddress(endpoint.Groups[1].Value);
            Debug.Log($"[Connect Phone] {output}");
        }

        // Not public API: backs EditorUserBuildSettings.m_AndroidDeviceSocketAddress, which Unity `adb connect`s
        // to when refreshing the Run Device list. Silently skipped if a Unity version renames it.
        static void SetDeviceSocketAddress(string address)
        {
            var property = typeof(EditorUserBuildSettings).GetProperty("androidDeviceSocketAddress",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanWrite)
                property.SetValue(null, address);
        }

        static string AdbPath()
        {
            var sdk = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath;
            if (!string.IsNullOrEmpty(sdk))
            {
                var adb = Path.Combine(sdk, "platform-tools", "adb.exe");
                if (File.Exists(adb))
                    return adb;
            }
            return Path.Combine(EditorApplication.applicationContentsPath,
                "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        }

        static (int exitCode, string output) Run(string adb, int timeoutMs)
        {
            var script = Path.GetFullPath(ScriptPath);
            if (!File.Exists(script))
                return (-1, $"Script not found: {script}");

            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -NonInteractive -Adb \"{adb}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                return (-1, "Timed out.");
            }
            return (process.ExitCode, (stdout.Result + stderr.Result).Trim());
        }
    }
}
#endif
