using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace XForge.Ide.Server.Services;

/// <summary>
/// Sessão de terminal real via ConPTY (pseudo-console do Windows).
/// Cria um PowerShell interativo cuja entrada/saída é ligada aos pipes do console.
/// Equivalente ao xterm.js no VS Code: PTY real, não um "exec" por comando.
/// </summary>
public sealed class ConPtySession : IAsyncDisposable
{
    private const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
    private const uint ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
    private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    private readonly ILogger _log;
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private IntPtr _ptyInputWrite = IntPtr.Zero;
    private IntPtr _ptyOutputRead = IntPtr.Zero;
    private IntPtr _console = IntPtr.Zero;
    private Process? _process;
    private CancellationTokenSource? _readerCts;

    /// <summary>Diretório de trabalho do terminal (raiz do projeto aberto na IDE).</summary>
    public string WorkingDirectory { get; }

    public ConPtySession(string workingDirectory, ILogger log)
    {
        WorkingDirectory = workingDirectory;
        _log = log;
    }

    public Task Exited => _exited.Task;

    /// <summary>Inicia o PowerShell interativo no pseudo-console.</summary>
    public async Task StartAsync(Action<string> onData, CancellationToken ct = default)
    {
        var (inputRead, inputWrite) = CreatePipe();
        var (outputRead, outputWrite) = CreatePipe();
        _ptyInputWrite = inputWrite;
        _ptyOutputRead = outputRead;

        var size = new Coord(120, 30);
        var hr = CreatePseudoConsole(size, inputRead, outputWrite, 0, out _console);
        if (hr != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole falhou: HRESULT 0x{hr:x8} (Win32 {Marshal.GetLastWin32Error()})");
        }

        var startupInfo = new StartupInfoEx();
        startupInfo.StartupInfo.dwFlags = EXTENDED_STARTUPINFO_PRESENT | STARTF_USESTDHANDLES;
        startupInfo.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();

        // Padrão documentado: 1) consulta o tamanho necessário com lpAttributeList=NULL,
        // 2) aloca, 3) inicializa. (Tamanho fixo 24/12 costuma falhar em x64.)
        var attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        startupInfo.lpAttributeList = Marshal.AllocHGlobal(attrSize);

        try
        {
            if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref attrSize))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"InitializeProcThreadAttributeList falhou: Win32 {err}");
            }
            if (!UpdateProcThreadAttribute(startupInfo.lpAttributeList, 0,
                    (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"UpdateProcThreadAttribute falhou: Win32 {err}");
            }

            // PowerShell sem banner; cwd na raiz do projeto
            var args = "-NoLogo -NoProfile -ExecutionPolicy Bypass";
            var info = new ProcessInformation();
            if (!CreateProcess(
                    lpApplicationName: null,
                    lpCommandLine: $"powershell.exe {args}",
                    lpProcessAttributes: IntPtr.Zero,
                    lpThreadAttributes: IntPtr.Zero,
                    bInheritHandles: false,
                    dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT,
                    lpEnvironment: IntPtr.Zero,
                    lpCurrentDirectory: Directory.Exists(WorkingDirectory) ? WorkingDirectory : null,
                    lpStartupInfo: ref startupInfo,
                    lpProcessInformation: out info))
            {
                var err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"CreateProcess falhou: Win32 {err}");
            }

            _process = Process.GetProcessById(info.dwProcessId);
            CloseHandleSafe(info.hThread);

            // bombeia a saída do PTY
            _readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => ReadLoopAsync(outputRead, onData, _readerCts.Token), _readerCts.Token);

            _ = Task.Run(async () =>
            {
                await _process.WaitForExitAsync(_readerCts.Token).ConfigureAwait(false);
                _exited.TrySetResult();
            }, _readerCts.Token);
        }
        finally
        {
            DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
            Marshal.FreeHGlobal(startupInfo.lpAttributeList);
        }

        await Task.CompletedTask;
    }

    private async Task ReadLoopAsync(IntPtr outputRead, Action<string> onData, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            uint read = 0;
            var ok = ReadFile(outputRead, buffer, (uint)buffer.Length, ref read, IntPtr.Zero);
            if (first)
            {
                first = false;
                _log.LogInformation("PTY primeira leitura: ok={Ok} bytes={Bytes} erro={Err}", ok, read, Marshal.GetLastWin32Error());
            }
            if (!ok || read == 0)
            {
                break;
            }
            onData(System.Text.Encoding.UTF8.GetString(buffer, 0, (int)read));
        }
        _exited.TrySetResult();
        await Task.CompletedTask;
    }

    /// <summary>Envia texto digitado no navegador para o PTY.</summary>
    public void Write(string data)
    {
        if (_ptyInputWrite == IntPtr.Zero || data.Length == 0)
        {
            return;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        WriteFile(_ptyInputWrite, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
    }

    /// <summary>Redimensiona o PTY (o xterm avisa o tamanho real do painel).</summary>
    public void Resize(int cols, int rows)
    {
        if (_console == IntPtr.Zero)
        {
            return;
        }
        ResizePseudoConsole(_console, new Coord((short)Math.Clamp(cols, 20, 500), (short)Math.Clamp(rows, 5, 200)));
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _readerCts?.Cancel();
        }
        catch
        {
        }

        try
        {
            _process?.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        if (_console != IntPtr.Zero)
        {
            ClosePseudoConsole(_console);
            _console = IntPtr.Zero;
        }
        SafeClose(_ptyInputWrite);
        SafeClose(_ptyOutputRead);
        _ptyInputWrite = _ptyOutputRead = IntPtr.Zero;

        try
        {
            if (_process is not null)
            {
                await _process.WaitForExitAsync(new CancellationTokenSource(2000).Token).ConfigureAwait(false);
            }
        }
        catch
        {
        }
        _process?.Dispose();
    }

    private static void SafeClose(IntPtr handle)
    {
        if (handle != IntPtr.Zero && handle != (IntPtr)(-1))
        {
            CloseHandle(handle);
        }
    }

    private static (IntPtr read, IntPtr write) CreatePipe()
    {
        if (!CreatePipe(out var read, out var write, IntPtr.Zero, 0))
        {
            throw new InvalidOperationException("CreatePipe falhou");
        }
        return (read, write);
    }

    private static void CloseHandleSafe(IntPtr h)
    {
        if (h != IntPtr.Zero && h != (IntPtr)(-1))
        {
            CloseHandle(h);
        }
    }

    // ------------------------------------------------------------------ P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Coord(short X, short Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPseudoConsole, Coord size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ClosePseudoConsole(IntPtr hPseudoConsole);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, uint nSize);

    // SIZE_T é pointer-sized (8 bytes em x64): usar IntPtr, nunca int, senão o
    // marshaller devolve lixo nos 4 bytes altos e a chamada falha.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, uint dwAttributeCount, uint dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize,
        IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName, string lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo, out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, ref uint lpNumberOfBytesRead, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
