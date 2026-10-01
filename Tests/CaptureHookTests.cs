using AQWMod.Localization.Core;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // CaptureHook é estático e compartilhado: roda isolado dos outros testes
    [Collection("CaptureHook")]
    public class CaptureHookTests
    {
        [Fact]
        public void Record_WithoutRecorder_IsNoOp()
        {
            CaptureHook.Recorder = null;
            CaptureHook.Record("k", "raw", "cat", "path", null, null, null, 1f, "tmp");
        }

        [Fact]
        public void Record_ForwardsArgumentsToRecorder()
        {
            string? seenKey = null, seenSource = null; float seenNow = 0;
            CaptureHook.Recorder = (key, raw, cat, path, p, n, m, now, src) =>
            { seenKey = key; seenNow = now; seenSource = src; };
            try
            {
                CaptureHook.Record("Attack", "Attack!", "skill", "a/b", null, null, null, 2.5f, "tmp");
                Assert.Equal("Attack", seenKey);
                Assert.Equal(2.5f, seenNow);
                Assert.Equal("tmp", seenSource);
            }
            finally { CaptureHook.Recorder = null; }
        }

        [Fact]
        public void NullCollector_DoesNothing()
        {
            var c = new NullMissingTranslationCollector();
            c.Capture("texto", null);
            c.FlushToDisk();
            Assert.Equal(0, c.PendingCount);
        }
    }
}
