using System;
using Xunit;

namespace ASR.IntegrationTests
{
    [Collection("ASR-collection")]
    public class AsrUnitTests
    {
        private readonly AsrFixture _fx;
        public AsrUnitTests(AsrFixture fx) { _fx = fx; }

        [Fact]
        public void Transcribe_WithValidInput_ShouldReturnText()
        {
            // Construct a simple 1-second 16 kHz silent audio (all zeros)
            float[] pcm = new float[16000];
            string text = WhisperNativeForTests.DoTranscribe(pcm);

            Assert.NotNull(text);
            // The return value may be an empty string (silence), but must not be null
        }

        [Fact]
        public void Transcribe_WithEmptyArray_ShouldReturnEmptyString()
        {
            float[] pcm = Array.Empty<float>();
            string text = WhisperNativeForTests.DoTranscribe(pcm);

            Assert.True(text == string.Empty || text == null || text.Length == 0,
                        "Empty input should return an empty string, not crash");
        }

        [Fact]
        public void Transcribe_WithNull_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                WhisperNativeForTests.DoTranscribe(null);
            });
        }

        [Fact(Timeout = 10000)] // 10-second safeguard
        public void Transcribe_Performance_ShouldFinishQuickly()
        {
            float[] pcm = new float[16000]; // 1-second silence
            string text = WhisperNativeForTests.DoTranscribe(pcm);

            // No requirement for non-empty text, just needs to complete within 10s
            Assert.NotNull(text);
        }
    }
}

