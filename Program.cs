namespace Translit;

internal static class Program
{
    private const string MutexName = "Transliterator_2C1F4E7A-9B3D-4A61-8E2C-7F0A1D6B5C39";

    [STAThread]
    private static int Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) {
            MessageBox.Show("Transliterator is already running.", "Transliterator", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());

        GC.KeepAlive(mutex);

        return 0;
    }
}