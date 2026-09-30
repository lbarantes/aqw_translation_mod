using System.Threading;

namespace AQWMod.Localization.Debug
{
    // Contadores de observabilidade. Interlocked em vez de lock, pra poder
    // incrementar de qualquer thread sem custo de sincronização.
    public sealed class TranslationStats
    {
        private long _requests;
        private long _cacheHits;
        private long _hits;
        private long _misses;
        private long _reloads;

        public long Requests  => Interlocked.Read(ref _requests);
        public long CacheHits => Interlocked.Read(ref _cacheHits);
        public long Hits      => Interlocked.Read(ref _hits);
        public long Misses    => Interlocked.Read(ref _misses);
        public long Reloads   => Interlocked.Read(ref _reloads);

        public void IncrementRequests()  => Interlocked.Increment(ref _requests);
        public void IncrementCacheHits() => Interlocked.Increment(ref _cacheHits);
        public void IncrementHits()      => Interlocked.Increment(ref _hits);
        public void IncrementMisses()    => Interlocked.Increment(ref _misses);
        public void IncrementReloads()   => Interlocked.Increment(ref _reloads);

        public void DumpToLog(TranslationLogger logger)
        {
            long req = Requests;
            if (req == 0) return;

            double cacheRate = req > 0 ? (double)CacheHits / req * 100.0 : 0;
            double hitRate   = req > 0 ? (double)Hits      / req * 100.0 : 0;
            double missRate  = req > 0 ? (double)Misses    / req * 100.0 : 0;

            logger.Info("═══════════════════════════════════════");
            logger.Info("         AQW Translation — Stats        ");
            logger.Info("═══════════════════════════════════════");
            logger.Info($"  Total requests  : {req}");
            logger.Info($"  Cache hits      : {CacheHits} ({cacheRate:F1}%)");
            logger.Info($"  Repo hits       : {Hits} ({hitRate:F1}%)");
            logger.Info($"  Misses          : {Misses} ({missRate:F1}%)");
            logger.Info($"  Hot reloads     : {Reloads}");
            logger.Info("═══════════════════════════════════════");
        }
    }
}
