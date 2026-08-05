using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace WaveBreakerGameplay.Audio
{
    [MyEntityComponentDescriptor(
        typeof(MyObjectBuilder_Jukebox),
        false)]
    public class WaveBreakerJukeboxBoost : MyGameLogicComponent
    {
        private const string VolumeTag = "v=";
        private const float MaximumVolume = 9.9f;

        private IMySoundBlock _block;
        private string _cachedCustomData = string.Empty;

        private int _tickCounter = 0;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            _block = Entity as IMySoundBlock;

            if (_block == null)
                return;

            NeedsUpdate = MyEntityUpdateEnum.EACH_10TH_FRAME;
        }

        public override void UpdateBeforeSimulation10()
        {
            try
            {
                if (_block == null)
                    return;

                if (!_block.IsWorking)
                    return;

                _tickCounter++;

                if (_tickCounter < 6)
                    return;

                _tickCounter = 0;

                if (_block.CustomData == _cachedCustomData)
                    return;

                _cachedCustomData = _block.CustomData;

                UpdateVolume();
            }
            catch (System.Exception exception)
            {
                MyLog.Default.WriteLine(
                    "[WaveBreaker] Audio exception: " +
                    exception.ToString());
            }
        }

        private void UpdateVolume()
        {
            string customData;
            int index;
            int endIndex;

            string valueString;
            float volume;

            customData = _block.CustomData;

            if (string.IsNullOrWhiteSpace(customData))
                return;

            index = customData.IndexOf(
                VolumeTag,
                System.StringComparison.OrdinalIgnoreCase);

            if (index < 0)
                return;

            index += VolumeTag.Length;

            endIndex = customData.IndexOf('\n', index);

            if (endIndex < 0)
                endIndex = customData.Length;

            valueString = customData.Substring(
                index,
                endIndex - index);

            valueString = valueString.Trim();

            if (!float.TryParse(
                    valueString,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out volume))
            {
                return;
            }

            if (float.IsNaN(volume))
                return;

            if (float.IsInfinity(volume))
                return;

            // Values below 1.0 use vanilla behaviour.
            if (volume < 1.0f)
                return;

            volume = MathHelper.Clamp(
                volume,
                1.0f,
                MaximumVolume);

            _block.Volume = volume;
        }

        public override void Close()
        {
            _block = null;

            base.Close();
        }
    }
}