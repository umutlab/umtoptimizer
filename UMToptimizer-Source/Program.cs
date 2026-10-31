using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using UMToptimizer.Services;

namespace UMToptimizer;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AllocConsole();
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (!AttachConsole(AttachParentProcess)) AllocConsole();
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            return CliRunner.RunAsync(args).GetAwaiter().GetResult();
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}
