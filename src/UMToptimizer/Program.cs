using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UMToptimizer;

internal static partial class Program
{
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AttachConsole(int pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AllocConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                return app.Run();
            }
            catch (Exception e)
            {
                // Tema/kaynak yüklenemedi gibi açılış hataları: ham çökme yerine tek, anlaşılır ileti.
                App.StartupFailed(e);
                return 1;
            }
        }

        // Argümanla çağrıldıysa: arayüz yerine komut satırı modu (WinExe olduğu için konsola elle bağlanılır).
        if (!AttachConsole(-1)) AllocConsole();
        var utf8 = new UTF8Encoding(false);
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true });
        return Core.Cli.RunAsync(args).GetAwaiter().GetResult();
    }
}
