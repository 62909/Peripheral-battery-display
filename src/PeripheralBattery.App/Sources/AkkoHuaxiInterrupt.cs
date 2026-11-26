using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.Native;

namespace PeripheralBattery.Sources;

/// <summary>
/// Overlapped ReadFile stays posted across WriteFile. The official page
/// does the same with its inputreport listener. HidD_GetInputReport and
/// HidD_SetOutputReport are control transfers and are not used here.
/// </summary>
internal sealed class AkkoHuaxiLink : IAkkoHuaxiLink, IDisposable
{
    private const int RetryPauseMs = 15;

    private readonly IntPtr _handle;
    private readonly CancellationToken _cancellation;
    private readonly byte[] _input;
    private readonly GCHandle _inputPin;
    private IntPtr _readEvent;
    private IntPtr _writeEvent;
    private IntPtr _readOverlapped;
    private IntPtr _writeOverlapped;
    private readonly Queue<byte[]> _queued = new();
    private bool _readPending;
    private bool _disposed;

    public AkkoHuaxiLink(IntPtr handle, int inputReportByteLength, CancellationToken cancellationToken)
    {
        _handle = handle;
        _cancellation = cancellationToken;
        var length = AkkoHuaxiExchange.InputBufferLength(inputReportByteLength);
        _input = new byte[length];
        if (length > 0)
            _inputPin = GCHandle.Alloc(_input, GCHandleType.Pinned);
        _readEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
        _writeEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, null);
        _readOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        _writeOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
    }

    public void FlushInput()
    {
        _queued.Clear();
        if (_handle != IntPtr.Zero && _handle != NativeMethods.InvalidHandle)
            NativeMethods.HidD_FlushQueue(_handle);
    }

    public bool ArmRead()
    {
        if (_readPending || _queued.Count > 0)
        {
            if (!_readPending)
                HoldOneRead();
            return _readPending || _queued.Count > 0;
        }

        return HoldOneRead();
    }

    public bool SendOutput(ReadOnlySpan<byte> report, int timeoutMs)
    {
        if (report.IsEmpty || timeoutMs <= 0 || !Usable(_writeEvent) || !Usable(_writeOverlapped))
            return false;
        var buffer = report.ToArray();
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Prepare(_writeOverlapped, _writeEvent);
            // TRUE means the write finished in this call. A pending write reports
            // its size later, through GetOverlappedResult.
            if (NativeMethods.WriteFile(_handle, pin.AddrOfPinnedObject(), buffer.Length, IntPtr.Zero, _writeOverlapped))
                return true;
            if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
                return false;
            return Wait(_writeEvent, _writeOverlapped, timeoutMs, out _);
        }
        finally
        {
            pin.Free();
        }
    }

    public bool WaitRead(int timeoutMs, out byte[]? report)
    {
        report = null;
        if (_queued.Count > 0)
        {
            report = _queued.Dequeue();
            return report.Length > 0;
        }

        if (!_readPending)
            return false;
        if (!Wait(_readEvent, _readOverlapped, timeoutMs, out var transferred))
        {
            _readPending = false;
            return false;
        }

        _readPending = false;
        report = CopyInput(transferred);
        return report.Length > 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_readPending)
        {
            Reap(_readOverlapped, out _);
            _readPending = false;
        }

        if (_inputPin.IsAllocated)
            _inputPin.Free();
        Free(ref _readOverlapped);
        Free(ref _writeOverlapped);
        Close(ref _readEvent);
        Close(ref _writeEvent);
    }

    private bool HoldOneRead()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (_cancellation.IsCancellationRequested)
                return _readPending || _queued.Count > 0;
            if (PostRead())
                return true;
            if (_queued.Count == 0)
            {
                PauseAfterMiss();
                return false;
            }
        }

        return _readPending || _queued.Count > 0;
    }

    private bool PostRead()
    {
        if (_input.Length == 0 || !_inputPin.IsAllocated || !Usable(_readEvent) || !Usable(_readOverlapped))
            return false;
        Array.Clear(_input);
        Prepare(_readOverlapped, _readEvent);
        if (NativeMethods.ReadFile(_handle, _inputPin.AddrOfPinnedObject(), _input.Length, IntPtr.Zero, _readOverlapped))
        {
            if (!Completed(_readOverlapped, out var transferred))
                transferred = _input.Length;
            _queued.Enqueue(CopyInput(transferred));
            return false;
        }

        if (Marshal.GetLastWin32Error() != NativeMethods.ErrorIoPending)
            return false;
        _readPending = true;
        return true;
    }

    private bool Wait(IntPtr ioEvent, IntPtr overlapped, int timeoutMs, out int transferred)
    {
        transferred = 0;
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (_cancellation.IsCancellationRequested)
                return Reap(overlapped, out transferred);
            var slice = (int)Math.Min(100, Math.Max(0, deadline - Environment.TickCount64));
            var status = NativeMethods.WaitForSingleObject(ioEvent, (uint)slice);
            if (status == NativeMethods.WaitObject0)
                return Completed(overlapped, out transferred);
            if (status != NativeMethods.WaitTimeout || Environment.TickCount64 >= deadline)
                return Reap(overlapped, out transferred);
        }
    }

    private bool Reap(IntPtr overlapped, out int transferred)
    {
        NativeMethods.CancelIoEx(_handle, overlapped);
        return Completed(overlapped, out transferred, wait: true);
    }

    private bool Completed(IntPtr overlapped, out int transferred, bool wait = false)
    {
        if (!NativeMethods.GetOverlappedResult(_handle, overlapped, out transferred, wait) || transferred <= 0)
        {
            transferred = 0;
            return false;
        }

        return true;
    }

    private byte[] CopyInput(int transferred)
    {
        if (transferred <= 0)
            return [];
        if (transferred > _input.Length)
            transferred = _input.Length;
        var copy = new byte[transferred];
        Buffer.BlockCopy(_input, 0, copy, 0, transferred);
        return copy;
    }

    private void PauseAfterMiss()
    {
        if (_cancellation.IsCancellationRequested)
            return;
        Thread.Sleep(RetryPauseMs);
    }

    private static void Prepare(IntPtr overlapped, IntPtr ioEvent)
    {
        NativeMethods.ResetEvent(ioEvent);
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = ioEvent }, overlapped, false);
    }

    private static bool Usable(IntPtr handle) =>
        handle != IntPtr.Zero && handle != NativeMethods.InvalidHandle;

    private static void Free(ref IntPtr memory)
    {
        if (memory == IntPtr.Zero)
            return;
        Marshal.FreeHGlobal(memory);
        memory = IntPtr.Zero;
    }

    private static void Close(ref IntPtr handle)
    {
        if (!Usable(handle))
        {
            handle = IntPtr.Zero;
            return;
        }

        NativeMethods.CloseHandle(handle);
        handle = IntPtr.Zero;
    }
}
