using System;
using System.Runtime.InteropServices;

static class MiniLMNative
{
    const string Dll = "MiniLMBridge.dll";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InitMiniLM(
        [MarshalAs(UnmanagedType.LPStr)] string modelXmlPath,
        [MarshalAs(UnmanagedType.LPStr)] string deviceName);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool InferMiniLM(
        long[] inputIds,
        long[] attentionMask,
        int length,
        float[] outEmbeddings);
}
