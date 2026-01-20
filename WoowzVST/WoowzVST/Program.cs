using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

class Program
{
    static void Main()
    {
        var enumerator = new MMDeviceEnumerator();

        // Захват с виртуального кабеля
        var cableOutput = GetDevice(enumerator, DataFlow.Capture, "CABLE Output");
        if (cableOutput == null) { Console.WriteLine("CABLE Output не найден!"); return; }
        Console.WriteLine($"Захват с кабеля: {cableOutput.FriendlyName}");

        // Вывод на физические колонки
        var speakers = GetDevice(enumerator, DataFlow.Render, "Динамики");
        if (speakers == null) { Console.WriteLine("Колонки не найдены!"); return; }
        Console.WriteLine($"Вывод на: {speakers.FriendlyName}");

        var capture = new WasapiCapture(cableOutput);
        capture.ShareMode = AudioClientShareMode.Shared;

        var buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true };

        capture.DataAvailable += (s, e) =>
        {
            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat) return;

            float[] floatBuffer = new float[e.BytesRecorded / 4];
            Buffer.BlockCopy(e.Buffer, 0, floatBuffer, 0, e.BytesRecorded);

            ProcessAudio(floatBuffer);

            byte[] outBytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(floatBuffer, 0, outBytes, 0, e.BytesRecorded);
            buffer.AddSamples(outBytes, 0, outBytes.Length);
        };

        using var waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 100);
        waveOut.Init(buffer);

        capture.StartRecording();
        waveOut.Play();

        Console.WriteLine("Эффект включен! Enter для выхода...");
        Console.ReadLine();

        capture.StopRecording();
        waveOut.Stop();
        capture.Dispose();
        Console.WriteLine("Готово!");
    }
    
    static MMDevice GetDevice(MMDeviceEnumerator enumerator, DataFlow flow, string nameContains){
        foreach (var dev in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            if (dev.FriendlyName.Contains(nameContains))
                return dev;
        return null;
    }
    
    static void ProcessAudio(float[] buffer){
        for (int i = 0; i < buffer.Length; i++){
            buffer[i] *= 8;
            if (buffer[i] > 1f) buffer[i] = 1f;
            if (buffer[i] < -1f) buffer[i] = -1f;
        }
    }
}