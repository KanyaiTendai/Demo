using System.Diagnostics;
using Reqnroll;

namespace Demo.Support;

[Binding]
public sealed class AllureReportHooks
{
    private const string AllureExecutable = "allure";
    private const int ReportPort = 4567;

    private static readonly string PidFilePath = Path.Combine(Path.GetTempPath(), "demo-allure-open.pid");

    [AfterTestRun]
    public static void GenerateAndOpenReport()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var resultsDirectory = Path.Combine(baseDirectory, "allure-results");
        var reportDirectory = Path.Combine(baseDirectory, "allure-report");

        if (!Directory.Exists(resultsDirectory))
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
        catch (Exception ex)
        {
            Console.WriteLine($"Allure report could not be generated/opened automatically: {ex.Message}");
        }
    }

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
