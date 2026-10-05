using System.ComponentModel;
using System.Diagnostics;
using Reqnroll;

namespace Demo.Support;

[Binding]
public sealed class AllureReportHooks
{
    private const string AllureExecutable = "allure";
    private const int ReportPort = 4567;

    private static readonly string PidFilePath = Path.Join(Path.GetTempPath(), "demo-allure-open.pid");

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

            RunAllure($"generate \"{resultsDirectory}\" --clean -o \"{reportDirectory}\"", waitForExit: true);

            var process = RunAllure($"open -p {ReportPort} \"{reportDirectory}\"", waitForExit: false);
            if (process is not null)
            {
                File.WriteAllText(PidFilePath, process.Id.ToString());
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

        if (int.TryParse(File.ReadAllText(PidFilePath), out var pid))
        {
            try
            {
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }

        File.Delete(PidFilePath);
    }

    private static Process? RunAllure(string arguments, bool waitForExit)
    {
        var process = Process.Start(new ProcessStartInfo(AllureExecutable, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (process is not null && waitForExit)
        {
            process.WaitForExit();
        }

        return process;
    }
}
