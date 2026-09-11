using System.Runtime.InteropServices;

namespace PrintTrack.Agent.Native;

/// <summary>
/// P/Invoke surface for the Windows print spooler (winspool.drv) used to observe and hold jobs.
/// Only the pieces the agent needs are declared.
/// </summary>
internal static partial class WinSpool
{
    // ---- printer change notification flags ----
    internal const uint PRINTER_CHANGE_ADD_JOB = 0x00000100;
    internal const uint PRINTER_CHANGE_SET_JOB = 0x00000200;
    internal const uint PRINTER_CHANGE_DELETE_JOB = 0x00000400;
    internal const uint PRINTER_CHANGE_WRITE_JOB = 0x00000800;
    internal const uint PRINTER_CHANGE_JOB =
        PRINTER_CHANGE_ADD_JOB | PRINTER_CHANGE_SET_JOB | PRINTER_CHANGE_DELETE_JOB | PRINTER_CHANGE_WRITE_JOB;

    // ---- SetJob commands ----
    internal const uint JOB_CONTROL_PAUSE = 1;
    internal const uint JOB_CONTROL_RESUME = 2;
    internal const uint JOB_CONTROL_CANCEL = 3;
    internal const uint JOB_CONTROL_RESTART = 4;
    internal const uint JOB_CONTROL_DELETE = 5;
    internal const uint JOB_CONTROL_RETAIN = 8;
    internal const uint JOB_CONTROL_RELEASE = 9;

    // ---- JOB_INFO_2 Status bits ----
    internal const uint JOB_STATUS_PAUSED = 0x00000001;
    internal const uint JOB_STATUS_ERROR = 0x00000002;
    internal const uint JOB_STATUS_DELETING = 0x00000004;
    internal const uint JOB_STATUS_SPOOLING = 0x00000008;
    internal const uint JOB_STATUS_PRINTING = 0x00000010;
    internal const uint JOB_STATUS_PRINTED = 0x00000080;
    internal const uint JOB_STATUS_DELETED = 0x00000100;
    internal const uint JOB_STATUS_BLOCKED_DEVQ = 0x00000200;
    internal const uint JOB_STATUS_RESTART = 0x00000800;
    internal const uint JOB_STATUS_COMPLETE = 0x00001000;

    // ---- WaitForSingleObject ----
    internal const uint WAIT_OBJECT_0 = 0x00000000;
    internal const uint WAIT_TIMEOUT = 0x00000102;
    internal const uint WAIT_FAILED = 0xFFFFFFFF;
    internal const uint INFINITE = 0xFFFFFFFF;

    // ---- DEVMODE ----
    internal const short DMCOLOR_MONOCHROME = 1;
    internal const short DMCOLOR_COLOR = 2;
    internal const short DMDUP_SIMPLEX = 1;

    internal const int DM_ORIENTATION = 0x00000001;
    internal const int DM_PAPERSIZE = 0x00000002;
    internal const int DM_COPIES = 0x00000100;
    internal const int DM_COLOR = 0x00000800;
    internal const int DM_DUPLEX = 0x00001000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;

        public readonly DateTimeOffset ToDateTimeOffset()
        {
            if (wYear == 0) return DateTimeOffset.UtcNow;
            try { return new DateTimeOffset(wYear, wMonth, wDay, wHour, wMinute, wSecond, wMilliseconds, TimeSpan.Zero); }
            catch { return DateTimeOffset.UtcNow; }
        }
    }

    /// <summary>
    /// JOB_INFO_2. String members are marshalled as LPWStr pointers into the buffer EnumJobs filled;
    /// pDevMode points at a DEVMODE we read separately.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct JOB_INFO_2
    {
        public uint JobId;
        [MarshalAs(UnmanagedType.LPWStr)] public string pPrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pMachineName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pUserName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDocument;
        [MarshalAs(UnmanagedType.LPWStr)] public string pNotifyName;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
        [MarshalAs(UnmanagedType.LPWStr)] public string pPrintProcessor;
        [MarshalAs(UnmanagedType.LPWStr)] public string pParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string pDriverName;
        public IntPtr pDevMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string pStatus;
        public IntPtr pSecurityDescriptor;
        public uint Status;
        public uint Priority;
        public uint Position;
        public uint StartTime;
        public uint UntilTime;
        public uint TotalPages;
        public uint Size;
        public SYSTEMTIME Submitted;
        public uint Time;
        public uint PagesPrinted;
    }

    /// <summary>Prefix of DEVMODEW — enough to read color/duplex/paper/copies.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public short dmOrientation;
        public short dmPaperSize;
        public short dmPaperLength;
        public short dmPaperWidth;
        public short dmScale;
        public short dmCopies;
        public short dmDefaultSource;
        public short dmPrintQuality;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PRINTER_DEFAULTS
    {
        public IntPtr pDatatype;
        public IntPtr pDevMode;
        public uint DesiredAccess;
    }

    internal const uint PRINTER_ACCESS_ADMINISTER = 0x00000004;
    internal const uint PRINTER_ACCESS_USE = 0x00000008;
    internal const uint STANDARD_RIGHTS_REQUIRED = 0x000F0000;
    internal const uint PRINTER_ALL_ACCESS =
        STANDARD_RIGHTS_REQUIRED | PRINTER_ACCESS_ADMINISTER | PRINTER_ACCESS_USE;

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ClosePrinter(IntPtr hPrinter);

    [LibraryImport("winspool.drv", SetLastError = true)]
    internal static partial IntPtr FindFirstPrinterChangeNotification(
        IntPtr hPrinter, uint fdwFilter, uint fdwOptions, IntPtr pPrinterNotifyOptions);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindNextPrinterChangeNotification(
        IntPtr hChange, out uint pdwChange, IntPtr pvReserved, IntPtr ppPrinterNotifyInfo);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FindClosePrinterChangeNotification(IntPtr hChange);

    [LibraryImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumJobs(
        IntPtr hPrinter, uint FirstJob, uint NoJobs, uint Level,
        IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

    [LibraryImport("winspool.drv", EntryPoint = "GetJobW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetJob(
        IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint cbBuf, out uint pcbNeeded);

    [LibraryImport("winspool.drv", EntryPoint = "SetJobW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetJob(IntPtr hPrinter, uint JobId, uint Level, IntPtr pJob, uint Command);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
}
