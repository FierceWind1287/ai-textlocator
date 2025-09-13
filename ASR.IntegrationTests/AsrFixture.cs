using System;
using System.IO;
using Xunit;

namespace ASR.IntegrationTests
{
    public class AsrFixture : IDisposable
    {
        private static bool _inited;

        public AsrFixture()
        {
            if (_inited) return;

            Environment.SetEnvironmentVariable("OV_LOG_LEVEL", "ERROR");

            string modelDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                           "extern", "distil-whisper-large");
            Assert.True(Directory.Exists(modelDir), "Model directory does not exist: " + modelDir);

            int rc = WhisperNativeForTests.Initialize(modelDir, "CPU");   // Use CPU first for stability
            Assert.Equal(0, rc);

            _inited = true;
        }

        public void Dispose() { }
    }
}
