using System;
using System.Runtime.InteropServices;

namespace TextLocator  // ← Ensure the namespace matches your project
{
    internal static class WhisperNative
    {
        // -------- DLL path --------
        // Since we already call SetDllDirectory(externPath) in App.xaml.cs,
        // we only need to specify the DLL name here.
        private const string DllName = "WhisperBridge.dll";

        // -------- Call C++ Init(modelDir, device) --------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Init(string modelDir, string device);

        // -------- Call C++ Transcribe --------
        // The return value is const char*; on the C# side we use IntPtr and convert to string.
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Transcribe(IntPtr pcm, int len);

        /// <summary>
        /// C#-friendly wrapper: input float[], output string
        /// </summary>
        internal static string Transcribe(float[] audio)
        {
            int len = audio.Length;
            // Pin the float[] so GC will not move it
            var handle = GCHandle.Alloc(audio, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();    // Get pointer to the array
                IntPtr pStr = Transcribe(ptr, len);         // Call into the DLL
                return Marshal.PtrToStringAnsi(pStr) ?? string.Empty; // Convert to string
            }
            finally
            {
                handle.Free(); // Always release the pinned handle
            }
        }
    }
}
