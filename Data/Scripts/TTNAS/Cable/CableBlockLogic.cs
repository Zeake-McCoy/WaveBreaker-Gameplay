using Sandbox.ModAPI;
using System;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

namespace TTNAS
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AdvancedDoor), true)]
    public class CableBlockLogic : MyGameLogicComponent
    {
        private IMyTerminalBlock      _block;
        private CableRendererInstance _renderer;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _block = Entity as IMyTerminalBlock;
            if (_block == null) return;

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame()
        {
            base.UpdateOnceBeforeFrame();

            if (MyAPIGateway.Utilities.IsDedicated) return;

            try
            {
                if (_block == null) return;

                CableBlockConfig cfg;
                if (!CableConfigs.TryGet(_block.BlockDefinition.SubtypeName, out cfg))
                    return;

                _renderer = new CableRendererInstance(Entity, cfg);
                NAS_Session.I?.RegisterCableRenderer(_renderer);

                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
            }
            catch (Exception e)
            {
                NASLog.Error("Cable", "Init error: " + e);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            base.UpdateBeforeSimulation();
            if (_renderer != null) _renderer.Update();
        }

        public override void Close()
        {
            base.Close();

            try
            {
                if (_renderer != null)
                    NAS_Session.I?.UnregisterCableRenderer(_renderer);
            }
            catch { }

            _renderer = null;
            _block    = null;
        }
    }
}
