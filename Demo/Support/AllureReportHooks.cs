using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Reqnroll;

namespace Demo.Support;

[Binding]
public sealed class AllureReportHooks
{
    private const string AllureExecutable = "allure";
    private const int ReportPort = 4567;

    private static readonly TimeSpan GenerateTimeout = TimeSpan.FromMinutes(2);

    // A per-user folder rather than the shared temp folder, so another account can't plant the file.
    private static readonly string PidFilePath = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "demo-allure-open.pid");

    [AfterTestRun]
    public static void GenerateAndOpenReport()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var resultsDirectory = Path.Join(baseDirectory, "allure-results");
        var reportDirectory = Path.Join(baseDirectory, "allure-report");

        // In CI the pipeline builds and publishes the report; a localhost server would be unreachable.
        if (!Directory.Exists(resultsDirectory) || IsRunningInCi())
        {
            return;
        }

        try
        {
            StopPreviousServer();

            using (var generate = RunAllure($"generate \"{resultsDirectory}\" --clean -o \"{reportDirectory}\""))
            {
                if (generate is not null && !generate.WaitForExit(GenerateTimeout))
                {
                    generate.Kill(entireProcessTree: true);
                    Console.WriteLine($"Allure report generation took longer than {GenerateTimeout.TotalMinutes} minutes and was stopped.");
                    return;
                }
            }

            var server = RunAllure($"open -p {ReportPort} \"{reportDirectory}\"");
            if (server is not null)
            {
                // The start time is saved with the id, so a later run only ever stops this exact process,
                // never an unrelated one that has since been given the same id by the operating system.
                File.WriteAllText(
                    PidFilePath,
                    $"{server.Id}|{server.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}");
                Console.WriteLine($"Allure report available at http://localhost:{ReportPort}");
            }
        }
        // Allure not installed (Win32Exception), the process exiting early (InvalidOperationException)
        // or the PID file being unwritable shouldn't fail the test run.
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Allure report could not be generated/opened automatically: {ex.Message}");
        }
    }

    private static bool IsRunningInCi() =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    private static void StopPreviousServer()
    {
        if (!File.Exists(PidFilePath))
        {
            return;
        }

        var parts = File.ReadAllText(PidFilePath).Split('|');
        File.Delete(PidFilePath);

        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var startTicks))
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.StartTime.ToUniversalTime().Ticks == startTicks)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Process already exited, or it belongs to someone else.
        }
    }

    private static Process? RunAllure(string arguments)
    {
        var process = Process.Start(new ProcessStartInfo(AllureExecutable, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (process is not null)
        {
            // Redirected output must be read, or Allure blocks once the pipe buffer fills and the
            // test run never finishes. Nothing needs it, so it is discarded.
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }
}
