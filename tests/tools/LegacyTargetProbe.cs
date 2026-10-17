// Old-style WinForms fixture: deliberately has no installer command or IPC server.
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class LegacyTargetProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2) return 9;
        var marker = Path.Combine(args[0], args[1]);
        bool committed = false;
        var form = new Form { Text = "Isolated legacy MyCodex fixture", ShowInTaskbar = false };
        form.Menu = new MainMenu(new[] { new MenuItem("File", new[] { new MenuItem("Exit") }) });
        form.Shown += (sender, e) =>
        {
            form.Hide();
            if (File.Exists(marker + ".disabled")) form.Enabled = false;
            File.WriteAllText(marker + ".ready", "hidden main window");
        };
        form.FormClosing += async (sender, e) =>
        {
            if (committed) return;
            File.WriteAllText(marker + ".close-request", e.CloseReason.ToString());
            if (File.Exists(marker + ".reject")) { e.Cancel = true; return; }
            // 7.0 saves synchronously in FormClosing; later versions prepare asynchronously.
            if (File.Exists(marker + ".sync"))
            {
                File.WriteAllText(marker + ".saved", "synchronous draft save");
                return;
            }
            e.Cancel = true;
            form.Enabled = false;
            // Later clients freeze the window, then commit their own exit.
            await Task.Delay(400);
            File.WriteAllText(marker + ".saved", "draft retained");
            committed = true;
            form.Close();
        };
        var timer = new Timer { Interval = 100 };
        timer.Tick += (sender, e) => { if (File.Exists(marker + ".exit")) { committed = true; form.Close(); } };
        timer.Start();
        Application.Run(form);
        timer.Dispose();
        return 0;
    }
}
