using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

class Program{
    static void Main(){
        var enumerator = new MMDeviceEnumerator();

        var cableOutput = GetDevice(enumerator, DataFlow.Capture, "CABLE Output");
        if (cableOutput == null) { Console.WriteLine("CABLE Output не найден!"); return; }
        Console.WriteLine($"Захват с кабеля: {cableOutput.FriendlyName}");

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
    
    static Random rnd = new Random();

    static int maxSeconds = 30;
    static int sampleRate = 48000;
    static int channels = 2;
    static float[] historyBuffer = new float[sampleRate * maxSeconds * channels];
    static int historyIndex = 0;

    static int repeatStartIndex = -1;
    static int repeatLength = 0;
    static int repeatCounter = 0;
    static int repeatOffset = 0;

    static void ProcessAudio(float[] buffer, int bufChannels = 2)
    {
        int bufLen = buffer.Length;

        for (int i = 0; i < bufLen; i += bufChannels)
        {
            for (int c = 0; c < bufChannels; c++)
            {
                int idx = i + c;
                float sample = buffer[idx];

                // --- Записываем новые сэмплы в круговой буфер истории ---
                historyBuffer[historyIndex] = sample;
                historyIndex = (historyIndex + 1) % historyBuffer.Length;

                // --- Решаем, будем ли воспроизводить кусок истории ---
                if (repeatCounter <= 0)
                {
                    if (rnd.NextDouble() < 0.02) // 2% шанс начать воспроизводить историю
                    {
                        // Длина повторяемого фрагмента: от 1 сэмпла до всей истории
                        repeatLength = rnd.Next(1, historyBuffer.Length);

                        // Случайный offset назад: от 0 до всей истории
                        repeatOffset = rnd.Next(0, historyBuffer.Length);

                        // Начало повторяемого сегмента в истории
                        repeatStartIndex = (historyIndex - repeatOffset + historyBuffer.Length) % historyBuffer.Length;
                        repeatCounter = repeatLength;
                    }
                }

                // --- Если воспроизводим историю ---
                if (repeatCounter > 0)
                {
                    sample = historyBuffer[repeatStartIndex];
                    repeatStartIndex = (repeatStartIndex + 1) % historyBuffer.Length;
                    repeatCounter--;
                }

                // Ограничение амплитуды [-1..1]
                if (sample > 1f) sample = 1f;
                if (sample < -1f) sample = -1f;

                buffer[idx] = sample;
            }
        }
    }
}