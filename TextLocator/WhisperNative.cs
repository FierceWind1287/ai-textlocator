using System;
using System.Runtime.InteropServices;

namespace TextLocator  // ← 确保名字空间跟项目一致
{
    internal static class WhisperNative
    {
        // -------- DLL 路径 --------
        // 因为我们在 App.xaml.cs 里已经调用 SetDllDirectory(externPath)
        // 所以这里只写 DLL 名即可
        private const string DllName = "WhisperBridge.dll";

        // -------- 调用 C++ Init(modelDir, device) --------
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Init(string modelDir, string device);

        // -------- 调用 C++ Transcribe --------
        // 返回值是 const char*，C# 端用 IntPtr 再转字符串
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Transcribe(IntPtr pcm, int len);

        /// <summary>
        /// C# 友好的包装：输入 float[]，输出 string
        /// </summary>
        internal static string Transcribe(float[] audio)
        {
            int len = audio.Length;
            // 把 float[] pin 住，获取指针
            var handle = GCHandle.Alloc(audio, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                IntPtr pStr = Transcribe(ptr, len);
                return Marshal.PtrToStringAnsi(pStr) ?? string.Empty;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
