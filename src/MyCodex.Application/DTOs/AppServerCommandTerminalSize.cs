using System;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerCommandTerminalSize
    {
        public AppServerCommandTerminalSize(ushort rows, ushort cols)
        {
            if (rows == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rows),
                    SharedLocalization.Text(
                        "AppServerCommandTerminalSize.Error.RowsRequired",
                        "Terminal rows must be greater than zero."));
            }

            if (cols == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cols),
                    SharedLocalization.Text(
                        "AppServerCommandTerminalSize.Error.ColumnsRequired",
                        "Terminal columns must be greater than zero."));
            }

            Rows = rows;
            Cols = cols;
        }

        public ushort Rows { get; private set; }

        public ushort Cols { get; private set; }
    }
}
