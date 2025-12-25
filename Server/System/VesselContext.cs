using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Server.System
{
    /// <summary>
    /// Context for vessel-related operations.
    /// Tracks removed vessel IDs to prevent stale messages from re-adding deleted vessels.
    /// </summary>
    public static class VesselContext
    {
        private const int MaxRemovedVesselsCount = 10000;
        private const int EvictionProbability = 50; // 2% chance (1/50)
        
        private static readonly Random _random = new Random();
        private static int _evictionInProgress = 0;

        /// <summary>
        /// Tracks removed vessel IDs to prevent them from being re-added.
        /// Uses ConcurrentDictionary for thread-safe access with DateTime for LRU eviction.
        /// </summary>
        public static ConcurrentDictionary<Guid, DateTime> RemovedVessels { get; } 
            = new ConcurrentDictionary<Guid, DateTime>();

        /// <summary>
        /// Adds a vessel to the removed list with probabilistic non-blocking eviction.
        /// </summary>
        public static void AddRemovedVessel(Guid vesselId)
        {
            RemovedVessels.TryAdd(vesselId, DateTime.UtcNow);
            
            // Probabilistic eviction - 2% chance, completely non-blocking
            if (_random.Next(EvictionProbability) == 0)
            {
                TriggerEvictionAsync();
            }
        }

        private static void TriggerEvictionAsync()
        {
            // Prevent concurrent evictions using atomic compare-exchange
            if (Interlocked.CompareExchange(ref _evictionInProgress, 1, 0) != 0)
                return;
            
            _ = Task.Run(() =>
            {
                try
                {
                    if (RemovedVessels.Count <= MaxRemovedVesselsCount) return;
                    
                    var toRemove = RemovedVessels
                        .OrderBy(kv => kv.Value)
                        .Take(RemovedVessels.Count - MaxRemovedVesselsCount + 1000)
                        .Select(kv => kv.Key)
                        .ToArray();
                    
                    foreach (var id in toRemove)
                        RemovedVessels.TryRemove(id, out _);
                }
                finally
                {
                    Interlocked.Exchange(ref _evictionInProgress, 0);
                }
            });
        }

        /// <summary>
        /// Checks if a vessel ID is in the removed list.
        /// </summary>
        public static bool IsVesselRemoved(Guid vesselId) 
            => RemovedVessels.ContainsKey(vesselId);
    }
}
