using System.Runtime.InteropServices;

namespace StorePOS.Platforms.Windows;

internal static class WindowsRawPrinter
{
    public static bool SendRaw(string printerName, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(printerName) || data.Length == 0)
            return false;

        if (!OpenPrinter(printerName, out var hPrinter, IntPtr.Zero))
            return false;

        try
        {
            var di = new DOCINFOA
            {
                pDocName = "Receipt",
                pDataType = "RAW",
            };

            if (!StartDocPrinter(hPrinter, 1, di))
                return false;

            try
            {
                if (!StartPagePrinter(hPrinter))
                    return false;

                try
                {
                    return WritePrinter(hPrinter, data, data.Length, out _);
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOCINFOA di);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private sealed class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName = "";

        [MarshalAs(UnmanagedType.LPStr)]
        public string pOutputFile = "";

        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType = "";
    }
}
