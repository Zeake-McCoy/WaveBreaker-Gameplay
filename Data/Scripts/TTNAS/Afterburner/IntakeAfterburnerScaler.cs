using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using SpaceEngineers.Game.ModAPI;

namespace TTNAS
{
    // Attach to ALL air vents; Init() filters to small-grid only.
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AirVent), false)]
    public class IntakeAfterburnerScaler : MyGameLogicComponent
    {
        private IMyAirVent              _vent;
        private MyResourceSinkComponent _sink;
        private float                   _blockDefMaxPower;
        private bool                    _inited;

        private const float AB_POWER_MULT = 2f; // +100 % when any afterburner is active on this grid

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            _vent = Entity as IMyAirVent;
            var grid = _vent?.CubeGrid as MyCubeGrid;
            // Only scale power on small-grid air vents (simulates intake compression boost)
            if (grid == null || grid.GridSizeEnum != MyCubeSize.Small || grid.Physics == null)
                return;
            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (_vent == null) return;
            _sink = Entity.Components.Get<MyResourceSinkComponent>();
            if (_sink == null) return;

            _blockDefMaxPower = _sink.MaxRequiredInputByType(MyResourceDistributorComponent.ElectricityId);
            _sink.SetRequiredInputFuncByType(MyResourceDistributorComponent.ElectricityId, PowerScale);
            _sink.Update();

            _inited = true;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateAfterSimulation10()
        {
            if (_inited && _sink != null)
                _sink.Update();
        }

        private float PowerScale()
        {
            if (_vent == null || _sink == null) return 0f;

            float baseCalc = 0f;
            if (_vent.OxygenSinkInfo.MaxRequiredInput > 0f)
                baseCalc = (_vent.GasOutputPerSecond / _vent.OxygenSinkInfo.MaxRequiredInput) * _blockDefMaxPower;

            var grid = _vent.CubeGrid as MyCubeGrid;
            var mgr  = AfterburnerManager.Instance;
            if (grid != null && mgr != null && mgr.IsAnyActive(grid.EntityId))
                baseCalc *= AB_POWER_MULT;

            return baseCalc;
        }
    }
}
