using System.ComponentModel;
using System.Runtime.InteropServices;
using PrintTrack.Shared;
using static PrintTrack.Agent.Native.WinSpool;

namespace PrintTrack.Agent.Native;

/// <summary>
/// Owns an open handle to one print queue plus its change-notification handle.
/// Not thread-safe: use one instance per watcher thread.
/// </summary>
public sealed class SpoolerPrinter : IDisposable
{
    public string PrinterName { get; }
    private IntPtr _hPrinter;
    private IntPtr _hChange = IntPtr.Zero;

    private SpoolerPrinter(string name, IntPtr hPrinter)
    {
        PrinterName = name;
        _hPrinter = hPrinter;
    }

    /// <summary>Open the queue with admin rights so the agent can pause/resume/delete jobs.</summary>
    public static SpoolerPrinter Open(string printerName)
    {
        var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
        if (!OpenPrinter(printerName, out var h, ref defaults))
        {
            // fall back to lower access (still lets us read jobs)
            var useDefaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
            if (!OpenPrinter(printerName, out h, ref useDefaults))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"OpenPrinter falló para '{printerName}'.");
        }
        return new SpoolerPrinter(printerName, h);
    }

    public void StartChangeNotifications()
    {
        _hChange = FindFirstPrinterChangeNotification(_hPrinter, PRINTER_CHANGE_JOB, 0, IntPtr.Zero);
        if (_hChange == IntPtr.Zero || _hChange == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastPInvokeError(),
                $"FindFirstPrinterChangeNotification falló para '{PrinterName}'.");
    }

    /// <summary>Blocks until the spooler signals a job change or the timeout elapses.</summary>
    public bool WaitForChange(int timeoutMs)
    {
        if (_hChange == IntPtr.Zero) throw new InvalidOperationException("StartChangeNotifications no fue llamado.");
        var r = WaitForSingleObject(_hChange, (uint)timeoutMs);
        // consume the notification so the event resets; we always re-enumerate afterwards
        FindNextPrinterChangeNotification(_hChange, out _, IntPtr.Zero, IntPtr.Zero);
        return r == WAIT_OBJECT_0;
    }

    public IReadOnlyList<SpoolJob> ListJobs()
    {
        WinSpool.EnumJobs(_hPrinter, 0, 256, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0) return [];

        var buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!WinSpool.EnumJobs(_hPrinter, 0, 256, 2, buf, needed, out _, out var returned))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumJobs falló.");

            var list = new List<SpoolJob>((int)returned);
            var stride = Marshal.SizeOf<JOB_INFO_2>();
            for (var i = 0; i < returned; i++)
            {
                var info = Marshal.PtrToStructure<JOB_INFO_2>(buf + i * stride);
                list.Add(Decode(info));
            }
            return list;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public SpoolJob? FindJob(uint jobId)
    {
        WinSpool.GetJob(_hPrinter, jobId, 2, IntPtr.Zero, 0, out var needed);
        if (needed == 0) return null;

        var buf = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!WinSpool.GetJob(_hPrinter, jobId, 2, buf, needed, out _))
                return null;
            return Decode(Marshal.PtrToStructure<JOB_INFO_2>(buf));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    public bool Pause(uint jobId) => SetJob(_hPrinter, jobId, 0, IntPtr.Zero, JOB_CONTROL_PAUSE);
    public bool Resume(uint jobId) => SetJob(_hPrinter, jobId, 0, IntPtr.Zero, JOB_CONTROL_RESUME);
    public bool Delete(uint jobId) => SetJob(_hPrinter, jobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE);

    private static SpoolJob Decode(in JOB_INFO_2 j)
    {
        var color = ColorMode.Unknown;
        var duplex = DuplexMode.Unknown;
        string? paper = null;
        var copies = 1;

        if (j.pDevMode != IntPtr.Zero)
        {
            try
            {
                var dm = Marshal.PtrToStructure<DEVMODE>(j.pDevMode);
                if ((dm.dmFields & DM_COLOR) != 0)
                    color = dm.dmColor == DMCOLOR_COLOR ? ColorMode.Color : ColorMode.Grayscale;
                if ((dm.dmFields & DM_DUPLEX) != 0)
                    duplex = dm.dmDuplex == DMDUP_SIMPLEX ? DuplexMode.Simplex : DuplexMode.Duplex;
                if ((dm.dmFields & DM_PAPERSIZE) != 0)
                    paper = PaperSizes.Name(dm.dmPaperSize);
                if ((dm.dmFields & DM_COPIES) != 0 && dm.dmCopies > 0)
                    copies = dm.dmCopies;
            }
            catch { /* driver-specific DEVMODE we can't read; leave as Unknown */ }
        }

        return new SpoolJob
        {
            JobId = j.JobId,
            PrinterName = j.pPrinterName ?? "",
            MachineName = j.pMachineName,
            UserName = j.pUserName ?? "",
            Document = string.IsNullOrEmpty(j.pDocument) ? "(sin nombre)" : j.pDocument,
            Datatype = j.pDatatype,
            StatusFlags = j.Status,
            TotalPages = (int)j.TotalPages,
            PagesPrinted = (int)j.PagesPrinted,
            SizeBytes = j.Size,
            SubmittedUtc = j.Submitted.ToDateTimeOffset(),
            Copies = copies,
            Color = color,
            Duplex = duplex,
            PaperSize = paper
        };
    }

    public void Dispose()
    {
        if (_hChange != IntPtr.Zero && _hChange != new IntPtr(-1))
        {
            FindClosePrinterChangeNotification(_hChange);
            _hChange = IntPtr.Zero;
        }
        if (_hPrinter != IntPtr.Zero)
        {
            ClosePrinter(_hPrinter);
            _hPrinter = IntPtr.Zero;
        }
    }
}
