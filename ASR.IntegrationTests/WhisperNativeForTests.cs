using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal static class WhisperNativeForTests
{
    private const string DllName = "WhisperBridge.dll";

    static WhisperNativeForTests()
    {
        // Allow the process to locate WhisperBridge / OpenVINO native DLLs in the extern directory
        string externDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extern");
        if (Directory.Exists(externDir))
        {
            // 1) Add directory to Windows loader search path
            try { SetDllDirectory(externDir); } catch { }

            // 2) Compatibility: also append to PATH
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                if (path.IndexOf(externDir, StringComparison.OrdinalIgnoreCase) < 0)
                    Environment.SetEnvironmentVariable("PATH", externDir + ";" + path);
            }
            catch { }

            // 3) Proactively LoadLibrary to detect missing dependencies early
            string full = Path.Combine(externDir, DllName);
            if (File.Exists(full))
            {
                IntPtr h = LoadLibrary(full);
                if (h == IntPtr.Zero)
                    Debug.WriteLine("[ASR] LoadLibrary failed: " + Marshal.GetLastWin32Error());
            }
        }
        else
        {
            Debug.WriteLine("[ASR] extern dir NOT FOUND: " + externDir);
        }
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    // C++: int Init(const char* modelDir, const char* device);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private static extern int Init(string modelDir, string device);

    // C++: const char* Transcribe(const float* pcm, int length);
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Transcribe(IntPtr pcm, int length);

    // If the C++ side exports a free function, keep this; otherwise, comment out the following two lines and its usage
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void FreeString(IntPtr p);

    public static int Initialize(string modelDir, string device)
    {
        if (string.IsNullOrWhiteSpace(modelDir))
            throw new ArgumentException("modelDir is null or empty.", nameof(modelDir));
        if (!Directory.Exists(modelDir))
            throw new DirectoryNotFoundException(modelDir);

        return Init(modelDir, device);
    }

    public static string DoTranscribe(float[] pcm)
    {
        if (pcm == null)                     // Match your unit test: null should throw an exception
            throw new ArgumentNullException(nameof(pcm));
        if (pcm.Length == 0)                 // Empty array: just return an empty string
            return string.Empty;

        var handle = GCHandle.Alloc(pcm, GCHandleType.Pinned);
        try
        {
            IntPtr p = handle.AddrOfPinnedObject();
            IntPtr ret = Transcribe(p, pcm.Length);
            if (ret == IntPtr.Zero) return string.Empty;

            // Your C++ returns an ANSI string
            string s = Marshal.PtrToStringAnsi(ret);

            // If free function is available, keep it; if not exported, comment out DllImport and this call
            try { FreeString(ret); } catch { /* ignore if not provided */ }

            return s ?? string.Empty;
        }
        finally
        {
            handle.Free();
        }
    }
}
