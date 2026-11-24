using System.Media;

namespace PomoQuote.Mascot;

/// <summary>
/// Plays a short WAV (16-bit PCM) at a chosen volume. <see cref="SoundPlayer"/> has no volume control, so
/// the samples are scaled in memory. Playback is asynchronous: <see cref="Play"/> returns at once.
/// </summary>
internal sealed class MascotSound : IDisposable
{
    private readonly byte[] _wav;
    private SoundPlayer? _player;
    private MemoryStream? _stream;
    private double _gain = -1;

    /// <summary>Reads the file; throws if it is missing or not a 16-bit PCM WAV.</summary>
    public MascotSound(string path)
    {
        _wav = File.ReadAllBytes(path);
        ScalePcm16(_wav, 1.0); // validates the format
    }

    /// <summary>Plays the sound at <paramref name="gain"/> (0 = silent, 1 = original level).</summary>
    public void Play(double gain)
    {
        gain = Math.Clamp(gain, 0, 1);
        if (gain <= 0) return;

        if (_player is null || Math.Abs(gain - _gain) > 0.001)
        {
            _player?.Dispose();
            _stream?.Dispose();
            _stream = new MemoryStream(ScalePcm16(_wav, gain));
            _player = new SoundPlayer(_stream);
            _gain = gain;
        }

        _player.Play();
    }

    /// <summary>
    /// Returns a copy of a 16-bit PCM WAV with every sample multiplied by <paramref name="gain"/>.
    /// Throws <see cref="InvalidDataException"/> for anything that is not a plain 16-bit PCM WAV.
    /// </summary>
    internal static byte[] ScalePcm16(byte[] wav, double gain)
    {
        if (wav.Length < 44 || !IsTag(wav, 0, "RIFF") || !IsTag(wav, 8, "WAVE")) throw new InvalidDataException("Not a WAV file.");

        var copy = (byte[])wav.Clone();
        var position = 12;
        var sawFormat = false;
        while (position + 8 <= copy.Length)
        {
            var size = BitConverter.ToInt32(copy, position + 4);
            if (size < 0) throw new InvalidDataException("Bad chunk size.");
            var body = position + 8;

            if (IsTag(copy, position, "fmt "))
            {
                var format = BitConverter.ToInt16(copy, body);
                var bits = BitConverter.ToInt16(copy, body + 14);
                if (format != 1 || bits != 16) throw new InvalidDataException("Only 16-bit PCM is supported.");
                sawFormat = true;
            }
            else if (IsTag(copy, position, "data"))
            {
                if (!sawFormat) throw new InvalidDataException("Data before format.");
                var end = Math.Min(copy.Length, body + size);
                for (var i = body; i + 1 < end; i += 2)
                {
                    var sample = BitConverter.ToInt16(copy, i);
                    var scaled = (short)Math.Clamp((int)Math.Round(sample * gain), short.MinValue, short.MaxValue);
                    copy[i] = (byte)(scaled & 0xFF);
                    copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }

                return copy;
            }

            position = body + size + (size & 1);
        }

        throw new InvalidDataException("No audio data.");
    }

    private static bool IsTag(byte[] data, int offset, string tag) =>
        offset + 4 <= data.Length && data[offset] == tag[0] && data[offset + 1] == tag[1] && data[offset + 2] == tag[2] && data[offset + 3] == tag[3];

    public void Dispose()
    {
        _player?.Dispose();
        _stream?.Dispose();
    }
}
