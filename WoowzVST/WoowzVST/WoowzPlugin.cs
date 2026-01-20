using System;
using AudioPlugSharp;

namespace TestVST;

public class WoowzPlugin : AudioPluginBase{
    public WoowzPlugin(){
        PluginName = "WoowzPlugin";
        PluginCategory = "Fx";
        PluginID = 0xF57654336AFC4EF8;
        
        SampleFormatsSupported = EAudioBitsPerSample.Bits32;
    }

    FloatAudioIOPort monoInput;
    FloatAudioIOPort monoOutput;
    
    public override void Initialize(){
        base.Initialize();
        
        InputPorts  = new AudioIOPort[] { monoInput = new FloatAudioIOPort("Mono Input", EAudioChannelConfiguration.Mono) };
        OutputPorts = new AudioIOPort[] { monoOutput = new FloatAudioIOPort("Mono Output", EAudioChannelConfiguration.Mono) };

        AddParameter(new DecibelParameter
        {
            ID = "gain",
            Name = "Gain",
            MaxValue = 12,
            ValueFormat = "{0:0.0} dB"
        });
    }

    public override void Process(){
        base.Process();
        
        Host.ProcessAllEvents();
        
        double gainDb = GetParameter("gain").ProcessValue;
        float linearGain = (float)AudioPluginParameter.DBToLinear(gainDb);

        ReadOnlySpan<float> inSamples = monoInput.GetAudioBuffer(0);
        Span<float> outSamples = monoOutput.GetAudioBuffer(0);

        for (int i = 0; i < inSamples.Length; i++){
            outSamples[i] = inSamples[i] * linearGain;
        }
    }
}