using Server.Log;
using Server.Settings.Structures;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System.Vessel
{
    /// <summary>
    /// We try to avoid working with protovessels as much as possible as they can be huge files.
    /// This class patches the vessel file with the information messages we receive about a position and other vessel properties.
    /// This way we send the whole vessel definition only when there are parts that have changed 
    /// </summary>
    public partial class VesselDataUpdater
    {
        #region Semaphore

        /// <summary>
        /// To not overwrite our own data we use a lock
        /// </summary>
        private static readonly ConcurrentDictionary<Guid, object> Semaphore = new ConcurrentDictionary<Guid, object>();

        #endregion

        #region Cleanup

        /// <summary>
        /// Cleans up all cached data for a removed vessel to prevent memory leaks.
        /// Semaphore cleanup is delayed to let in-flight Task.Run operations complete.
        /// </summary>
        public static void CleanupVessel(Guid vesselId)
        {
            // Immediately clean throttle dictionaries
            LastPositionUpdateDictionary.TryRemove(vesselId, out _);
            LastFlightStateUpdateDictionary.TryRemove(vesselId, out _);
            LastResourcesUpdateDictionary.TryRemove(vesselId, out _);
            LastUpdateDictionary.TryRemove(vesselId, out _);
            
            // Delay semaphore cleanup to let in-flight tasks complete (~100ms grace period)
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                Semaphore.TryRemove(vesselId, out _);
            });
        }

        #endregion

        /// <summary>
        /// Raw updates a vessel in the dictionary and takes care of the locking in case we received another vessel message type
        /// </summary>
        public static void RawConfigNodeInsertOrUpdate(Guid vesselId, string vesselDataInConfigNodeFormat)
        {
            Task.Run(() =>
            {
                var vessel = new Classes.Vessel(vesselDataInConfigNodeFormat);
                if (GeneralSettings.SettingsStore.ModControl)
                {
                    var vesselParts = vessel.Parts.GetAllValues().Select(p => p.Fields.GetSingle("name").Value);
                    var bannedParts = vesselParts.Except(ModFileSystem.ModControl.AllowedParts);
                    if (bannedParts.Any())
                    {
                        LunaLog.Warning($"Received a vessel with BANNED parts! {vesselId}");
                        return;
                    }
                }
                lock (Semaphore.GetOrAdd(vesselId, new object()))
                {
                    VesselStoreSystem.CurrentVessels.AddOrUpdate(vesselId, vessel, (key, existingVal) => vessel);
                }
            });
        }
    }
}
