using HelloWorldBot.Mascot;

namespace HelloWorldBot.Tests;

public class MascotSoundTests
{
    /// <summary>A minimal mono 22.05 kHz 16-bit PCM WAV, optionally with an extra LIST chunk before the data.</summary>
    private static byte[] Wav(short[] samples, bool withListChunk = false, short format = 1, short bits = 16)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var list = withListChunk ? new byte[] { 1, 2, 3 } : [];
        var listSize = withListChunk ? 8 + list.Length + (list.Length & 1) : 0;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(4 + 24 + listSize + 8 + samples.Length * 2);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write(format);
        writer.Write((short)1);
        writer.Write(22050);
        writer.Write(44100);
        writer.Write((short)2);
        writer.Write(bits);
        if (withListChunk)
        {
            writer.Write("LIST"u8.ToArray());
            writer.Write(list.Length);
            writer.Write(list);
            if ((list.Length & 1) == 1) writer.Write((byte)0);
        }

        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write(sample);
        return stream.ToArray();
    }

    private static short[] Samples(byte[] wav)
    {
        var offset = Array.FindIndex(Enumerable.Range(0, wav.Length - 3).ToArray(), i => wav[i] == 'd' && wav[i + 1] == 'a' && wav[i + 2] == 't' && wav[i + 3] == 'a') + 8;
        return Enumerable.Range(0, (wav.Length - offset) / 2).Select(i => BitConverter.ToInt16(wav, offset + i * 2)).ToArray();
    }

    [Fact]
    public void Full_gain_keeps_the_samples()
    {
        var result = MascotSound.ScalePcm16(Wav([0, 1000, -1000, 32767, -32768]), 1.0);

        Assert.Equal([0, 1000, -1000, 32767, -32768], Samples(result));
    }

    [Fact]
    public void Half_gain_halves_the_samples()
    {
        var result = MascotSound.ScalePcm16(Wav([0, 1000, -1000, 20000]), 0.5);

        Assert.Equal([0, 500, -500, 10000], Samples(result));
    }

    [Fact]
    public void Zero_gain_is_silence()
    {
        Assert.All(Samples(MascotSound.ScalePcm16(Wav([5, -5, 30000]), 0.0)), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void A_gain_above_one_clips_instead_of_wrapping_round()
    {
        var result = MascotSound.ScalePcm16(Wav([30000, -30000]), 2.0);

        Assert.Equal([short.MaxValue, short.MinValue], Samples(result));
    }

    [Fact]
    public void The_original_bytes_are_not_changed()
    {
        var wav = Wav([1000, 2000]);
        var copy = (byte[])wav.Clone();

        MascotSound.ScalePcm16(wav, 0.1);

        Assert.Equal(copy, wav);
    }

    [Fact]
    public void Extra_chunks_before_the_audio_data_are_skipped()
    {
        var result = MascotSound.ScalePcm16(Wav([1000, -1000], withListChunk: true), 0.5);

        Assert.Equal([500, -500], Samples(result));
    }

    [Fact]
    public void The_header_is_left_alone()
    {
        var wav = Wav([1000, 2000, 3000]);

        var result = MascotSound.ScalePcm16(wav, 0.25);

        Assert.Equal(wav.Take(wav.Length - 6), result.Take(wav.Length - 6));
        Assert.Equal(wav.Length, result.Length);
    }

    [Theory]
    [InlineData(3, 16)] // float format
    [InlineData(1, 8)] // 8-bit
    [InlineData(1, 24)]
    public void Anything_but_16_bit_pcm_is_rejected(int format, int bits)
    {
        Assert.Throws<InvalidDataException>(() => MascotSound.ScalePcm16(Wav([1, 2, 3], format: (short)format, bits: (short)bits), 1.0));
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => MascotSound.ScalePcm16(new byte[100], 1.0));
        Assert.Throws<InvalidDataException>(() => MascotSound.ScalePcm16([1, 2, 3], 1.0));
    }

    [Fact]
    public void The_bundled_shout_is_a_valid_16_bit_wav_of_about_a_second()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot", "shout.wav");

        var result = MascotSound.ScalePcm16(File.ReadAllBytes(path), 0.5);

        var seconds = Samples(result).Length / 22050.0;
        Assert.InRange(seconds, 0.5, 2.0);
    }
}
