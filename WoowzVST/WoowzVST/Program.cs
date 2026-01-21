using System;
using System.Collections.Generic;
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

    // Текущий повтор истории
    static int repeatStartIndex = -1;
    static int repeatLength = 0;
    static int repeatCounter = 0;
    static float playbackSpeed = 1f; // скорость воспроизведения
    static float speedIndex = 0f;     // дробный индекс для интерполяции

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
                    if (rnd.NextDouble() < 0.10) // шанс начать повтор
                    {
                        // Случайная позиция
                        int randomOffset = rnd.Next(0, historyBuffer.Length);
                        repeatStartIndex = (historyIndex - randomOffset + historyBuffer.Length) % historyBuffer.Length;

                        // Длина повтора: 1..5 секунд
                        int minLength = (int)(sampleRate * 0.25f) * bufChannels;
                        int maxLength = sampleRate * 10 * bufChannels;
                        int remaining = (historyBuffer.Length - repeatStartIndex + historyBuffer.Length) % historyBuffer.Length;
                        int maxAllowed = Math.Min(maxLength, remaining);
                        repeatLength = rnd.Next(minLength, maxAllowed + 1);

                        // Сбрасываем счетчик и скорость
                        repeatCounter = repeatLength;
                        playbackSpeed = (float)(0.75 + rnd.NextDouble()/2); // 0.5..1.5×
                        speedIndex = 0f;
                    }
                }

                // --- Если воспроизводим историю ---
                if (repeatCounter > 0)
                {
                    // Интерполяция для скорости
                    int idx1 = repeatStartIndex + (int)speedIndex;
                    int idx2 = (idx1 + 1) % historyBuffer.Length;
                    float t = speedIndex - (int)speedIndex;
                    sample = historyBuffer[idx1 % historyBuffer.Length] * (1 - t) +
                             historyBuffer[idx2 % historyBuffer.Length] * t;

                    speedIndex += playbackSpeed;
                    repeatStartIndex = (repeatStartIndex + (int)playbackSpeed) % historyBuffer.Length;
                    repeatCounter--;

                    // Если вышли за предел истории, сброс
                    if (repeatCounter <= 0 || repeatStartIndex >= historyBuffer.Length)
                        repeatCounter = 0;
                }

                // Ограничение амплитуды [-1..1]
                if (sample > 1f) sample = 1f;
                if (sample < -1f) sample = -1f;

                buffer[idx] = sample;
            }
        }
    }
}