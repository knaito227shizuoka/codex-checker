using System.Threading;
using System.Windows.Forms;

namespace CodexChecker;

internal static class Program
{
    private const string MutexName = "Global\\CodexCheckerResident";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            using var context = new ResidentContext();
            Application.Run(context);
        }
        catch (HotKeyRegistrationException ex)
        {
            MessageBox.Show(ex.Message, "codex-checker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (CodexCommandNotFoundException ex)
        {
            MessageBox.Show(ex.Message, "codex-checker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
