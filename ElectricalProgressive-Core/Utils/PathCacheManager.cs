using System;
using System.Collections.Concurrent;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// A global, network‐independent path cache with TTL-based eviction.
    /// </summary>
    public static class PathCacheManager
    {
        private class Entry
        {
            public BlockPos[] Path;
            public int[] FacingFrom;
            public bool[][] NowProcessedFaces;
            public Facing[] UsedConnections;
            public DateTime LastAccessed;
        }

        // TTL after which unused entries are removed
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(1);

        // The actual cache keyed only by (start, end)
        private static readonly ConcurrentDictionary<(BlockPos, BlockPos, int), Entry> cache
            = new ConcurrentDictionary<(BlockPos, BlockPos, int), Entry>();

        /// <summary>
        /// Attempt to retrieve a cached path.
        /// </summary>
        public static bool TryGet(
            BlockPos start, BlockPos end, int currentVersion,
            out BlockPos[] path,
            out int[] facingFrom,
            out bool[][] nowProcessed,
            out Facing[] usedConnections)
        {
            var key = (start, end, currentVersion);
            if (cache.TryGetValue(key, out var entry) )
            {
                entry.LastAccessed = DateTime.UtcNow;
                path = entry.Path;
                facingFrom = entry.FacingFrom;
                nowProcessed = entry.NowProcessedFaces;
                usedConnections = entry.UsedConnections;
                return true;
            }

            path = null!;
            facingFrom = null!;
            nowProcessed = null!;
            usedConnections = null!;
            return false;
        }

        /// <summary>
        /// Store a newly computed path in the cache (or update existing).
        /// </summary>
        public static void AddOrUpdate(
            BlockPos start, BlockPos end, int currentVersion,
            BlockPos[] path,
            int[] facingFrom,
            bool[][] nowProcessedFaces,
            Facing[] usedConnections)
        {
            var key = (start, end, currentVersion);
            var entry = new Entry
            {
                Path = path,
                FacingFrom = facingFrom,
                NowProcessedFaces = nowProcessedFaces,
                UsedConnections = usedConnections,
                LastAccessed = DateTime.UtcNow
            };
            cache.AddOrUpdate(key, entry, (_, __) => entry);
        }

        /// <summary>
        /// Remove all entries not accessed within the TTL.
        /// Call periodically (e.g. once per second or every N ticks).
        /// </summary>
        public static void Cleanup()
        {
            var cutoff = DateTime.UtcNow - EntryTtl;
            foreach (var kvp in cache)
            {
                if (kvp.Value.LastAccessed < cutoff)
                {
                    cache.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
