using System.Runtime.InteropServices;
using OpenCTS.Core;

namespace OpenCTS.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && !(args.Length == 2 && args[0] == "--open"))
        {
            return RunCli(args);
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(args.Length == 2 ? args[1] : null));
        return 0;
    }

    internal static string FormatIssue(ValidationIssue issue)
    {
        string location = issue.Location is null
            ? string.Empty
            : $" line {issue.Location.Line}, column {issue.Location.Column}";
        string code = string.IsNullOrWhiteSpace(issue.Code)
            ? string.Empty
            : $" {issue.Code}";

        return $"{issue.Severity}{code} {issue.JsonPath}{location}: {issue.Message}";
    }

    private static int RunCli(string[] args)
    {
        ConsoleBridge.AttachToParent();

        if (args.Length == 2 && string.Equals(args[0], "--emit-aliases", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                foreach (string path in ScratchAsmCatalogExporter.WriteArtifacts(args[1]))
                {
                    Console.WriteLine($"Wrote {path}");
                }

                return 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        bool overwrite = args.Contains("--overwrite", StringComparer.OrdinalIgnoreCase);
        args = args.Where(arg => !arg.Equals("--overwrite", StringComparison.OrdinalIgnoreCase)).ToArray();
        bool decompile = args.Length == 3 && args[0].Equals("--decompile", StringComparison.OrdinalIgnoreCase);
        if (decompile) args = args[1..];
        bool hasRepairSwitch = args.Length > 0 &&
            string.Equals(args[0], "--repair", StringComparison.OrdinalIgnoreCase);
        bool attemptSafeRepair = args.Length == 3 && hasRepairSwitch;
        bool isStandardConversion = args.Length == 2 && !hasRepairSwitch;
        if (!isStandardConversion && !attemptSafeRepair)
        {
            Console.Error.WriteLine("Usage: ScratchASM <input .sasm|.mono|.sb3|project.json|folder> <output.sb3>");
            Console.Error.WriteLine("       ScratchASM --repair <input .sasm|.mono|.sb3|project.json|folder> <output.sb3>");
            Console.Error.WriteLine("       ScratchASM --emit-aliases <output-folder>");
            Console.Error.WriteLine("       ScratchASM [--decompile] <input.sb3> <output.sasm> [--overwrite]");
            Console.Error.WriteLine("       ScratchASM --open <input.sasm|input.sb3>");
            return 2;
        }

        string inputPath = attemptSafeRepair ? args[1] : args[0];
        string outputPath = attemptSafeRepair ? args[2] : args[1];
        ConversionOptions options = new() { AttemptSafeRepair = attemptSafeRepair, Overwrite = overwrite };
        ConversionResult result = decompile || Path.GetExtension(outputPath).Equals(".sasm", StringComparison.OrdinalIgnoreCase)
            ? new ScratchProjectConverter().ConvertToScratchAsm(inputPath, outputPath, overwrite)
            : new ScratchProjectConverter().ConvertToSb3(inputPath, outputPath, options);
        if (result.Success)
        {
            Console.WriteLine($"Wrote {result.OutputPath}");
            foreach (ValidationIssue issue in result.Issues)
            {
                Console.Error.WriteLine(FormatIssue(issue));
            }

            return 0;
        }

        foreach (ValidationIssue issue in result.Issues)
        {
            Console.Error.WriteLine(FormatIssue(issue));
        }

        return 1;
    }

    private static class ConsoleBridge
    {
        private const int AttachParentProcess = -1;

        public static void AttachToParent()
        {
            AttachConsole(AttachParentProcess);
            ResetConsoleStreams();
        }

        private static void ResetConsoleStreams()
        {
            try
            {
                Stream standardOutput = Console.OpenStandardOutput();
                Stream standardError = Console.OpenStandardError();
                Console.SetOut(new StreamWriter(standardOutput) { AutoFlush = true });
                Console.SetError(new StreamWriter(standardError) { AutoFlush = true });
            }
            catch (IOException)
            {
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
    }
}
