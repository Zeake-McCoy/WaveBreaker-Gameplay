using Sandbox.ModAPI;
using System;
using VRage;
using VRage.Game.Components;

namespace TTNAS
{
    // ───────────────────────────────────────────────────────────────────────────
    // NASApiSession — cross-mod registration endpoint.
    //
    // External mods can register configs into TTNAS at runtime without touching
    // this code.  Two registration paths are supported:
    //
    // ── Path A: same-mod / addon (simplest) ──────────────────────────────────
    //   Call the static registry directly from your session component's LoadData():
    //
    //     AfterburnerConfigs.Register(new AfterburnerConfig {
    //         SubtypeId       = "MyThruster",
    //         ParticleEffect  = "MyParticle",
    //         SoundEffect     = "MySound",
    //         PowerMultiplier = 2.0f,
    //         ThrustMultiplier= 1.5f,
    //         ParticleScale   = 1.0f
    //     });
    //
    //   Works when your mod loads alongside TTNavalAdvancedSystems in the same
    //   modlist (e.g. a local addon or a content pack).
    //
    // ── Path B: separate workshop mod (cross-mod message API) ────────────────
    //   Copy the NASApiClient_Template.cs file into your mod (removing "_Template"
    //   from the filename) and call from your LoadData():
    //
    //     NASApiClient.RegisterAfterburner(new NASApiClient.AfterburnerConfigData {
    //         SubtypeId = "MyThruster", ...
    //     });
    //
    //   TTNAS listens on channel 47299.  Messages must arrive as:
    //     MyTuple<string, byte[]>
    //     Item1 = "AfterburnerConfig" | "ThrustReverserConfig"
    //     Item2 = ProtoBuf-serialized config (matching ProtoMember numbers)
    //
    // Handler is registered in LoadData() (runs first for all mods).
    // External mods must send their messages in BeforeStart(), which fires
    // after all LoadData() calls have completed — guaranteeing the handler exists.
    // ───────────────────────────────────────────────────────────────────────────
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class NASApiSession : MySessionComponentBase
    {
        public const long Channel = 47299;

        public override void LoadData()
        {
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel, HandleMessage);
            NASLog.Info("API", $"Listening for external registrations on channel {Channel}.");
        }

        protected override void UnloadData()
        {
            MyAPIGateway.Utilities.UnregisterMessageHandler(Channel, HandleMessage);
        }

        private static void HandleMessage(object obj)
        {
            try
            {
                if (!(obj is MyTuple<string, byte[]>)) return;
                var tuple = (MyTuple<string, byte[]>)obj;

                string type = tuple.Item1;
                byte[] data = tuple.Item2;

                switch (type)
                {
                    case "AfterburnerConfig":
                        var ab = MyAPIGateway.Utilities.SerializeFromBinary<AfterburnerConfig>(data);
                        AfterburnerConfigs.Register(ab);
                        NASLog.Info("API", $"Registered AfterburnerConfig: {ab?.SubtypeId}");
                        break;

                    case "ThrustReverserConfig":
                        var rev = MyAPIGateway.Utilities.SerializeFromBinary<ThrustReverserConfig>(data);
                        ThrustReverserConfigs.Register(rev);
                        NASLog.Info("API", $"Registered ThrustReverserConfig: {rev?.SubtypeId}");
                        break;

                    case "CatapultConfig":
                        var cat = MyAPIGateway.Utilities.SerializeFromBinary<CatapultConfig>(data);
                        if (cat?.SubtypeId != null)
                        {
                            CatapultConfigs.Register(cat.SubtypeId, cat);
                            NASLog.Info("API", $"Registered CatapultConfig: {cat.SubtypeId}");
                        }
                        break;

                    case "AnchorBlockConfig":
                        var anc = MyAPIGateway.Utilities.SerializeFromBinary<AnchorBlockConfig>(data);
                        AnchorConfigs.Register(anc);
                        NASLog.Info("API", $"Registered AnchorBlockConfig: {anc?.SubtypeId}");
                        break;

                    case "CableBlockConfig":
                        var cab = MyAPIGateway.Utilities.SerializeFromBinary<CableBlockConfig>(data);
                        CableConfigs.AddOrReplace(cab);
                        NASLog.Info("API", $"Registered CableBlockConfig: {cab?.SubtypeId}");
                        break;

                    default:
                        NASLog.Warn("API", $"Unrecognised registration type: '{type}'");
                        break;
                }
            }
            catch (Exception e)
            {
                NASLog.Error("API", "HandleMessage error: " + e);
            }
        }
    }
}
