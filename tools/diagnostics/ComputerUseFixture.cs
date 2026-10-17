using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class ComputerUseFixture
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathRooted(args[0])) return;
        var signal = args[0];
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new Form
        {
            Text = "MyCodex Computer Use fixture",
            AccessibleName = "Computer Use fixture",
            ClientSize = new Size(620, 280),
            MinimumSize = new Size(520, 280),
            StartPosition = FormStartPosition.CenterScreen,
            Font = new Font("Segoe UI", 12)
        })
        using (var lifetime = new Timer { Interval = 250 })
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5, TabStop = false };
            var label = new Label { Text = "Test text (&T)", AutoSize = true, TabIndex = 0, TabStop = false };
            var input = new TextBox { AccessibleName = "Fixture input", Dock = DockStyle.Top, TabIndex = 1 };
            var submit = new Button { Text = "&Record test", AccessibleName = "Record test", AutoSize = true, TabIndex = 2 };
            var status = new TextBox { Text = "No record yet", AccessibleName = "Test result", ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 3 };
            var close = new Button { Text = "&Close", AccessibleName = "Close", AutoSize = true, TabIndex = 4 };
            submit.Click += delegate { status.Text = "Recorded: " + input.Text; File.WriteAllText(signal + ".result", status.Text); };
            close.Click += delegate { form.Close(); };
            layout.Controls.Add(label);
            layout.Controls.Add(input);
            layout.Controls.Add(submit);
            layout.Controls.Add(status);
            layout.Controls.Add(close);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Controls.Add(layout);
            form.Shown += delegate { input.Focus(); File.WriteAllText(signal, form.Handle.ToInt64().ToString()); };
            var started = DateTime.UtcNow;
            lifetime.Tick += delegate { if (File.Exists(signal + ".stop") || DateTime.UtcNow - started > TimeSpan.FromMinutes(5)) form.Close(); };
            lifetime.Start();
            Application.Run(form);
        }
    }
}
