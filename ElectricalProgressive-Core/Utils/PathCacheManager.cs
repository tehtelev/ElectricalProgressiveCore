using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Глобальный, не зависящий от сети кэш путей с удалением по TTL.
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

        // TTL, по истечении которого неиспользуемые записи удаляются
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(1);

        // Сам кэш, ключом служит только (start, end, version)
        private static readonly ConcurrentDictionary<(BlockPos, BlockPos, int), Entry> cache
            = new ConcurrentDictionary<(BlockPos, BlockPos, int), Entry>();

        /// <summary>
        /// Попытаться получить путь из кэша.
        /// </summary>
        public static bool TryGet(
            BlockPos start, BlockPos end, int currentVersion,
            out BlockPos[] path,
            out int[] facingFrom,
            out bool[][] nowProcessed,
            out Facing[] usedConnections)
        {
            var key = (start, end, currentVersion);
            if (cache.TryGetValue(key, out var entry))
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
        /// Сохранить в кэше новый вычисленный путь (или обновить существующий).
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
        /// Удалить все записи, к которым не обращались в течение TTL.
        /// Вызывать периодически (например, раз в секунду или каждые N тиков).
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



        /// <summary>
        /// Принудительно удаляет из кэша все записи для указанных координат start и end
        /// независимо от version.
        /// </summary>
        public static void RemoveAll(BlockPos start, BlockPos end)
        {
            // Собираем ключи, которые нужно удалить, чтобы не модифицировать
            // словарь прямо во время перебора.
            var keysToRemove = new List<(BlockPos, BlockPos, int)>();
            foreach (var kvp in cache)
            {
                var key = kvp.Key;
                if (key.Item1.Equals(start) && key.Item2.Equals(end))
                {
                    keysToRemove.Add(key);
                }
            }

            // Удаляем все собранные ключи
            foreach (var key in keysToRemove)
            {
                cache.TryRemove(key, out _);
            }
        }

    }
}
