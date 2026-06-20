using System;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;
using Jakaria.API;

namespace TTNavalBlocks.Logic
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), true, new string[]{"TTfullblockprop","TT1x1slope2x1baseprop","TThalfblockprop", "LXN_VectorProp_01"})]
    public class TorpedoThrusterComponent : MyGameLogicComponent
    {
        private MyThrust thruster;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            thruster = (MyThrust)Entity;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }



        public override void UpdateAfterSimulation100()
        {
            if (WaterModAPI.IsUnderwater(thruster.PositionComp.GetPosition()))
            {
                ((IMyThrust)thruster).ThrustMultiplier = 1;
            }
            else
            {
                ((IMyThrust)thruster).ThrustMultiplier = 0.05f;
            }
        }


    }
}
