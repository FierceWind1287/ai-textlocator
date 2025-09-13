using System;
using System.IO;
using Xunit;

namespace ASR.IntegrationTests
{
    [Collection("ASR-collection")]
    public class AsrSmokeTests : IClassFixture<AsrFixture>
    {
        private readonly AsrFixture _fx;
        public AsrSmokeTests(AsrFixture fx) { _fx = fx; }

        [Fact(Timeout = 120000)]
        public void Transcribe_Hello16k_ShouldReturn_Text()
        {
            string wav = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                      "testdata", "hello16k.wav"); // or how_are_you_doing_today.wav
            Assert.True(File.Exists(wav), "Missing testdata/hello16k.wav (16kHz/mono/16-bit PCM)");

            var pcm = Wav16kMonoToFloat(wav);
            string text = WhisperNativeForTests.DoTranscribe(pcm);

            Assert.False(string.IsNullOrWhiteSpace(text), "ASR returned empty text");
            Console.WriteLine("ASR => " + text);
        }

        // Read 16k/Mono/16-bit PCM
        private static float[] Wav16kMonoToFloat(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                string riff = new string(br.ReadChars(4)); if (riff != "RIFF") throw new InvalidDataException();
                br.ReadInt32();
                string wave = new string(br.ReadChars(4)); if (wave != "WAVE") throw new InvalidDataException();

                string id = new string(br.ReadChars(4));
                while (id != "fmt ")
                {
                    int skip = br.ReadInt32(); fs.Seek(skip, SeekOrigin.Current);
                    id = new string(br.ReadChars(4));
                }
                int fmtLen = br.ReadInt32();
                short fmt = br.ReadInt16(); short ch = br.ReadInt16(); int sr = br.ReadInt32();
                br.ReadInt32(); br.ReadInt16(); short bps = br.ReadInt16();
                if (fmtLen > 16) fs.Seek(fmtLen - 16, SeekOrigin.Current);
                if (fmt != 1 || ch != 1 || sr != 16000 || bps != 16)
                    throw new InvalidDataException("Requires 16kHz/Mono/16-bit PCM");

                id = new string(br.ReadChars(4));
                while (id != "data")
                {
                    int skip = br.ReadInt32(); fs.Seek(skip, SeekOrigin.Current);
                    id = new string(br.ReadChars(4));
                }
                int dataLen = br.ReadInt32();
                int n = dataLen / 2;
                var arr = new float[n];
                for (int i = 0; i < n; i++) arr[i] = br.ReadInt16() / 32768f;
                return arr;
            }
        }
    }
}
