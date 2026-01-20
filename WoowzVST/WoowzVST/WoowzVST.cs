using System;
using SharpSoundDevice;

namespace WoowzVST
{
    public class WoowzVST : IAudioDevice
    {
        private readonly DeviceInfo _devInfo;

        public WoowzVST()
        {
            _devInfo = new DeviceInfo
            {
                Developer = "Woowz11",
                DeviceID = "Woowz11 - WoowzPlugin",
                Name = "WoowzPlugin",
                Type = DeviceType.Effect,
                ProgramCount = 1,
                Version = 1000,
                HasEditor = false, // пока без GUI
                EditorWidth = 0,
                EditorHeight = 0
            };
            _devInfo.VstId = DeviceUtilities.GenerateIntegerId(_devInfo.DeviceID);

            ParameterInfo = Array.Empty<Parameter>();
            PortInfo = Array.Empty<Port>();
        }

        // === IAudioDevice свойства ===
        public DeviceInfo DeviceInfo => _devInfo;
        private int _deviceId;
        public int DeviceId
        {
            get => _deviceId;
            set => _deviceId = value;
        }
        public Parameter[] ParameterInfo { get; private set; }
        public Port[] PortInfo { get; private set; }
        public int CurrentProgram => 0;
        public IHostInfo HostInfo { get; set; }

        // === IAudioDevice методы ===
        public void InitializeDevice() { }
        public void DisposeDevice() { }
        public void Start() { }
        public void Stop() { }
        public bool SendEvent(Event ev) => false;

        // Простая обработка — просто копирует вход в выход
        public unsafe void ProcessSample(IntPtr input, IntPtr output, uint inChannelCount, uint outChannelCount, uint bufferSize)
        {
            if (inChannelCount == 0 || outChannelCount == 0) return;

            float* inPtr = (float*)input;
            float* outPtr = (float*)output;

            uint channels = Math.Min(inChannelCount, outChannelCount);

            for (int i = 0; i < bufferSize; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    outPtr[i * channels + ch] = inPtr[i * channels + ch];
                }
            }
        }

        public void ProcessSample(double[][] input, double[][] output, uint bufferSize)
        {
            uint channels = Math.Min((uint)input.Length, (uint)output.Length);
            for (int i = 0; i < bufferSize; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                    output[ch][i] = input[ch][i];
            }
        }

        public void OpenEditor(IntPtr parentWindow) { }
        public void CloseEditor() { }
        public void HostChanged() { }

        public Program GetProgramData(int index)
        {
            return new Program { Name = "Default", Data = Array.Empty<byte>() };
        }

        public void SetProgramData(Program program, int index) { }
    }
}