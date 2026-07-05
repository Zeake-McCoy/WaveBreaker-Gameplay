using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Components;

namespace TTNAS
{
    /// <summary>
    /// Tracks active afterburners per grid so other blocks (e.g. intake air vents)
    /// can query whether any afterburner is burning.
    ///
    /// Also publishes the per-grid active count on the Heat API channel so other mods
    /// (DYG_NavalDetection's thermal/IR model) can read afterburner state without an
    /// assembly reference. This bridge used to live in DYG_Air_supportblocks; it moved
    /// here when the afterburner logic was relocated into the TTNAS framework. The channel
    /// and message shape (MyTuple&lt;long gridId, int count&gt;) must stay identical to what
    /// consumers expect (AfterburnerApi.Channel = 1769300042).
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class AfterburnerManager : MySessionComponentBase
    {
        // Heat API: pushes MyTuple<long gridEntityId, int activeCount> on every change.
        // A consumer may send the request string to get the full current state re-pushed.
        public const long   HeatApiChannel = 1769300042;
        public const string HeatApiRequest = "DYG_AB_HeatApi_Req";

        private static AfterburnerManager _instance;
        public  static AfterburnerManager  Instance => _instance;

        // gridEntityId → count of currently-burning afterburners on that grid
        private readonly Dictionary<long, int> _activeCounts = new Dictionary<long, int>();

        public override void LoadData()
        {
            _instance = this;
            MyAPIGateway.Utilities.RegisterMessageHandler(HeatApiChannel, OnHeatApiMessage);
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(HeatApiChannel, OnHeatApiMessage);
            _activeCounts.Clear();
            _instance = null;
        }

        // A consumer mod loaded after us asks for the current picture — re-push everything.
        private void OnHeatApiMessage(object obj)
        {
            if (obj as string != HeatApiRequest)
                return;
            foreach (var kv in _activeCounts)
                MyAPIGateway.Utilities.SendModMessage(HeatApiChannel, new MyTuple<long, int>(kv.Key, kv.Value));
        }

        /// <summary>Called by each AfterburnerController when its burning state changes.</summary>
        public void SetThrusterActive(MyCubeGrid grid, bool active)
        {
            if (grid == null) return;
            long id = grid.EntityId;
            int count;
            _activeCounts.TryGetValue(id, out count);

            count = active ? count + 1 : System.Math.Max(0, count - 1);
            _activeCounts[id] = count;
            MyAPIGateway.Utilities.SendModMessage(HeatApiChannel, new MyTuple<long, int>(id, count));
        }

        public int  GetActiveCount(long gridEntityId)
        {
            int v;
            return _activeCounts.TryGetValue(gridEntityId, out v) ? v : 0;
        }

        public bool IsAnyActive(long gridEntityId) => GetActiveCount(gridEntityId) > 0;
    }
}
