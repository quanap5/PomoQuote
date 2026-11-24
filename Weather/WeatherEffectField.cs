using System.Drawing.Drawing2D;

namespace PomoQuote.Weather;

/// <summary>
/// The animation behind the widget: a small pool of particles per layer, advanced by time and drawn with one cached brush and
/// one cached pen. Nothing is allocated while it runs (the pools, the pen and the brush are made once), the particle count is
/// bounded by <see cref="MaxParticles"/>, and every colour is faint (see <see cref="PeakAlpha"/>) so the text on top stays
/// legible. When the effect changes the old layer fades out while the new one fades in (<see cref="FadeSeconds"/>).
/// Coordinates are logical pixels on the widget's 280 px grid; <see cref="Draw"/> scales them by the display unit.
/// </summary>
internal sealed class WeatherEffectField : IDisposable
{
    /// <summary>The most particles one layer ever holds (a few dozen).</summary>
    public const int MaxParticles = 48;

    /// <summary>The longest dt one update advances, so a stall (a sleep, a debugger) never makes a jump.</summary>
    public const float MaxStep = 0.1f;

    /// <summary>The seconds a change of effect takes to cross-fade.</summary>
    public const float FadeSeconds = 1.2f;

    /// <summary>The highest opacity (0 to 255) any single particle is drawn with: the text contrast budget is built on it.</summary>
    public const int PeakAlpha = 48;

    /// <summary>The opacity of a streak or a flake, which can cross another one: two of them stacked still keep the text readable (the contrast test measures it).</summary>
    public const int CrossingAlpha = 33;

    /// <summary>The opacity of the whole-widget flash of a thunderstorm at its brightest.</summary>
    public const int FlashAlpha = 20;

    /// <summary>How long one flash lasts.</summary>
    public const float FlashSeconds = 1.4f;

    private struct Particle
    {
        public float X, Y, Vx, Vy, Size, Phase, Rate;
    }

    private sealed class Layer
    {
        public readonly Particle[] Particles = new Particle[MaxParticles];
        public int Count;
        public WeatherEffect Effect = WeatherEffect.None;
        public float Opacity;
        public float Target;
    }

    private Layer _front = new();
    private Layer _back = new();
    private readonly Random _random;
    private readonly SolidBrush _brush = new(Color.Transparent);
    private readonly Pen _pen = new(Color.Transparent, 1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private float _width = 280;
    private float _height = 210;
    private float _time;
    private WeatherEffectIntensity _intensity = WeatherEffectIntensity.Low;
    private float _flashCountdown = 6;
    private float _flashAge = 99;

    public WeatherEffectField(int seed = 53) => _random = new Random(seed);

    /// <summary>The effect being shown (the one fading in, or at full strength).</summary>
    public WeatherEffect Effect => _front.Effect;

    /// <summary>The number of particles of the shown effect.</summary>
    public int ParticleCount => _front.Count;

    /// <summary>The number of particles over both layers (what a frame draws at most).</summary>
    public int TotalParticleCount => _front.Count + (_back.Opacity > 0 ? _back.Count : 0);

    /// <summary>The shown layer's opacity (0 while it fades in, 1 when it is done).</summary>
    public float Opacity => _front.Opacity;

    /// <summary>True when a frame would draw something.</summary>
    public bool HasVisibleEffect => (_front.Effect.Kind != WeatherEffectKind.None && _front.Opacity > 0) || (_back.Effect.Kind != WeatherEffectKind.None && _back.Opacity > 0);

    /// <summary>True while a cross-fade is under way.</summary>
    public bool IsFading => _front.Opacity < _front.Target || _back.Opacity > 0;

    /// <summary>True while every particle of the shown layers is inside the widget plus <paramref name="margin"> logical px on each side (clouds and haze may sit half outside).</summary>
    public bool ParticlesWithin(float margin)
    {
        foreach (var layer in new[] { _front, _back })
        {
            for (var i = 0; i < layer.Count; i++)
            {
                ref var p = ref layer.Particles[i];
                var extra = layer.Effect.Kind is WeatherEffectKind.Clouds or WeatherEffectKind.Haze ? p.Size : 0;
                if (p.X < -margin - extra || p.X > _width + margin + extra || p.Y < -margin - 20 || p.Y > _height + margin) return false;
            }
        }

        return true;
    }

    /// <summary>The widget size in logical px; the particles wrap inside it.</summary>
    public void SetSize(float width, float height) => (_width, _height) = (Math.Max(40, width), Math.Max(40, height));

    /// <summary>Switches to a new effect or intensity. The old effect fades out and the new one fades in, unless <paramref name="instant"/> (a still picture).</summary>
    public void Set(WeatherEffect effect, WeatherEffectIntensity intensity, bool instant = false)
    {
        if (effect == _front.Effect && intensity == _intensity)
        {
            if (instant) _front.Opacity = _front.Target;
            return;
        }

        _intensity = intensity;
        _back.Effect = WeatherEffect.None; // the layer that was still fading out is dropped
        _back.Opacity = 0;
        _back.Count = 0;
        (_front, _back) = (_back, _front);
        _back.Target = 0;
        if (instant) _back.Opacity = 0;

        Seed(_front, effect);
        _front.Target = effect.Kind == WeatherEffectKind.None ? 0 : 1;
        _front.Opacity = instant ? _front.Target : 0;
        _flashCountdown = 5 + (float)_random.NextDouble() * 6;
        _flashAge = 99;
    }

    /// <summary>Advances the animation by <paramref name="seconds"/> (at most <see cref="MaxStep"/>).</summary>
    public void Update(float seconds)
    {
        var dt = Math.Clamp(seconds, 0, MaxStep);
        _time += dt;
        Fade(_front, dt);
        Fade(_back, dt);
        if (_back.Opacity <= 0)
        {
            _back.Effect = WeatherEffect.None;
            _back.Count = 0;
        }
        Move(_front, dt);
        if (_back.Opacity > 0) Move(_back, dt);

        if (_front.Effect.Lightning && _front.Opacity > 0.5f)
        {
            _flashAge += dt;
            _flashCountdown -= dt;
            if (_flashCountdown <= 0)
            {
                _flashAge = 0;
                _flashCountdown = 7 + (float)_random.NextDouble() * 12; // at most one flash every 7 seconds
            }
        }
    }

    /// <summary>Moves the animation on by <paramref name="seconds"/> in steps (to give a still picture some development).</summary>
    public void Advance(float seconds)
    {
        for (var left = seconds; left > 0; left -= MaxStep) Update(Math.Min(left, MaxStep));
    }

    /// <summary>The flash's brightness now, 0 to 1: a soft rise (0.5 s) and a slow fall (0.9 s), never a strobe.</summary>
    public float FlashLevel
    {
        get
        {
            if (!_front.Effect.Lightning || _flashAge >= FlashSeconds) return 0;
            var x = _flashAge / FlashSeconds;
            var envelope = x < 0.35f ? x / 0.35f : 1 - (x - 0.35f) / 0.65f;
            return envelope * envelope * (3 - 2 * envelope) * _front.Opacity; // smoothstep
        }
    }

    // ---------- seeding and motion ----------

    private float Random01() => (float)_random.NextDouble();

    private int CountFor(WeatherEffect effect)
    {
        var level = (int)effect.Level;
        var normal = effect.Kind switch
        {
            WeatherEffectKind.SunGlints => 12,
            WeatherEffectKind.Stars => 22,
            WeatherEffectKind.Clouds => 2 + 2 * level, // 4, 6, 8
            WeatherEffectKind.Haze => 3 + level, // 4, 5, 6
            WeatherEffectKind.Rain => level switch { 1 => 14, 2 => 28, _ => 44 },
            WeatherEffectKind.Snow => level switch { 1 => 12, 2 => 22, _ => 34 },
            _ => 0,
        };
        var count = _intensity == WeatherEffectIntensity.Normal ? normal : (int)Math.Ceiling(normal * 0.55);
        return Math.Clamp(count, 0, MaxParticles);
    }

    private void Seed(Layer layer, WeatherEffect effect)
    {
        layer.Effect = effect;
        layer.Count = effect.Kind == WeatherEffectKind.None ? 0 : CountFor(effect);
        for (var i = 0; i < layer.Count; i++)
        {
            ref var p = ref layer.Particles[i];
            p = default;
            p.X = Random01() * _width;
            p.Y = Random01() * _height;
            p.Phase = Random01() * MathF.Tau;
            switch (effect.Kind)
            {
                case WeatherEffectKind.Rain:
                    p.Vy = RainSpeed(effect.Level) * (0.85f + Random01() * 0.3f);
                    p.Vx = -p.Vy * 0.16f;
                    p.Size = (effect.Level == EffectLevel.Light ? 5f : effect.Level == EffectLevel.Moderate ? 8f : 11f) * (0.8f + Random01() * 0.4f);
                    break;
                case WeatherEffectKind.Snow:
                    p.Vy = (12 + 7 * (int)effect.Level) * (0.7f + Random01() * 0.6f);
                    p.Size = 1.1f + Random01() * 1.3f;
                    p.Rate = 0.6f + Random01() * 0.9f; // the sway frequency
                    p.Vx = 3 + Random01() * 5; // the sway amplitude
                    break;
                case WeatherEffectKind.Clouds:
                    // One band per cloud: a point is covered by at most two clouds, so the cloud opacity stays bounded.
                    p.Y = (i + 0.5f) / layer.Count * _height;
                    p.Size = 70 + Random01() * 60; // width
                    p.Vx = (3 + Random01() * 4) * (i % 2 == 0 ? 1 : 0.7f);
                    break;
                case WeatherEffectKind.Haze:
                    p.Y = (i + 0.5f) / layer.Count * _height;
                    p.Size = 150 + Random01() * 90;
                    p.Vx = 2 + Random01() * 2.5f;
                    break;
                case WeatherEffectKind.SunGlints:
                case WeatherEffectKind.Stars:
                    SpreadOut(layer, i);
                    p.Size = effect.Kind == WeatherEffectKind.Stars ? 0.7f + Random01() * 0.9f : 1f + Random01() * 1.2f;
                    p.Rate = 0.6f + Random01() * 1.1f; // the twinkle frequency in turns per... radians per second
                    p.Vx = effect.Kind == WeatherEffectKind.SunGlints ? 1.5f : 0;
                    p.Vy = effect.Kind == WeatherEffectKind.SunGlints ? 1.0f : 0;
                    break;
            }
        }
    }

    /// <summary>Moves particle <paramref name="index"/> to a spot at least 14 logical px from the ones before it, so two glints never stack their opacity.</summary>
    private void SpreadOut(Layer layer, int index)
    {
        ref var p = ref layer.Particles[index];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var clear = true;
            for (var j = 0; j < index && clear; j++)
            {
                var dx = layer.Particles[j].X - p.X;
                var dy = layer.Particles[j].Y - p.Y;
                clear = dx * dx + dy * dy >= 14 * 14;
            }

            if (clear) return;
            p.X = Random01() * _width;
            p.Y = Random01() * _height;
        }
    }

    private static float RainSpeed(EffectLevel level) => level switch { EffectLevel.Light => 170, EffectLevel.Moderate => 250, _ => 330 };

    private void Fade(Layer layer, float dt)
    {
        var step = dt / FadeSeconds;
        layer.Opacity = layer.Opacity < layer.Target ? Math.Min(layer.Target, layer.Opacity + step) : Math.Max(layer.Target, layer.Opacity - step);
    }

    private void Move(Layer layer, float dt)
    {
        for (var i = 0; i < layer.Count; i++)
        {
            ref var p = ref layer.Particles[i];
            switch (layer.Effect.Kind)
            {
                case WeatherEffectKind.Rain:
                case WeatherEffectKind.Snow:
                    p.Y += p.Vy * dt;
                    if (layer.Effect.Kind == WeatherEffectKind.Rain) p.X += p.Vx * dt;
                    if (p.Y > _height + 12)
                    {
                        p.Y = -12 - Random01() * 20;
                        p.X = Random01() * _width;
                    }

                    if (p.X < -12) p.X += _width + 24;
                    break;
                case WeatherEffectKind.Clouds:
                case WeatherEffectKind.Haze:
                    p.X += p.Vx * dt;
                    if (p.X - p.Size * 0.6f > _width) p.X = -p.Size * 0.6f;
                    break;
                case WeatherEffectKind.SunGlints:
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    if (p.X > _width + 4) p.X = -4;
                    if (p.Y > _height + 4) p.Y = -4;
                    break;
            }
        }
    }

    // ---------- drawing ----------

    /// <summary>
    /// Draws the effect (both layers while they cross-fade) over what is already in <paramref name="graphics"/>. The caller has
    /// cleared the background; the widget's content is drawn on top afterwards.
    /// </summary>
    public void Draw(Graphics graphics, float unit)
    {
        if (_back.Opacity > 0) DrawLayer(graphics, _back, unit);
        if (_front.Opacity > 0) DrawLayer(graphics, _front, unit);

        var flash = FlashLevel;
        if (flash > 0.01f)
        {
            _brush.Color = Color.FromArgb((int)(FlashAlpha * flash), 214, 226, 255);
            graphics.FillRectangle(_brush, 0, 0, _width * unit, _height * unit);
        }
    }

    private int A(float alpha, float opacity) => Math.Clamp((int)(alpha * opacity), 0, 255);

    private void DrawLayer(Graphics g, Layer layer, float u)
    {
        var o = layer.Opacity;
        var day = layer.Effect.IsDay;
        switch (layer.Effect.Kind)
        {
            case WeatherEffectKind.Rain:
            {
                var (r, gr, b) = layer.Effect.Icy ? (196, 228, 255) : day ? (160, 196, 238) : (130, 168, 220);
                _pen.Width = Math.Max(1.2f, 1.1f * u);
                for (var i = 0; i < layer.Count; i++)
                {
                    ref var p = ref layer.Particles[i];
                    _pen.Color = Color.FromArgb(A(CrossingAlpha + 4, o), r, gr, b);
                    var tail = p.Size / MathF.Sqrt(1 + 0.16f * 0.16f);
                    g.DrawLine(_pen, p.X * u, p.Y * u, (p.X - tail * 0.16f) * u, (p.Y - tail) * u);
                }

                break;
            }

            case WeatherEffectKind.Snow:
            {
                var (r, gr, b) = day ? (232, 240, 252) : (206, 220, 244);
                for (var i = 0; i < layer.Count; i++)
                {
                    ref var p = ref layer.Particles[i];
                    var x = p.X + MathF.Sin(_time * p.Rate + p.Phase) * p.Vx;
                    _brush.Color = Color.FromArgb(A(CrossingAlpha, o), r, gr, b);
                    var s = p.Size * u;
                    g.FillEllipse(_brush, x * u - s, p.Y * u - s, 2 * s, 2 * s);
                }

                break;
            }

            case WeatherEffectKind.Clouds:
            {
                var (r, gr, b) = day ? (206, 220, 240) : (150, 166, 198);
                _brush.Color = Color.FromArgb(A(14, o), r, gr, b);
                for (var i = 0; i < layer.Count; i++)
                {
                    ref var p = ref layer.Particles[i];
                    var w = p.Size;
                    var h = w * 0.34f;
                    // Three overlapping puffs: left, middle (the tallest) and right.
                    g.FillEllipse(_brush, (p.X - w * 0.5f) * u, (p.Y - h * 0.35f) * u, w * 0.5f * u, h * 0.8f * u);
                    g.FillEllipse(_brush, (p.X - w * 0.3f) * u, (p.Y - h * 0.6f) * u, w * 0.6f * u, h * u);
                    g.FillEllipse(_brush, (p.X + w * 0.05f) * u, (p.Y - h * 0.3f) * u, w * 0.45f * u, h * 0.8f * u);
                }

                break;
            }

            case WeatherEffectKind.Haze:
            {
                var (r, gr, b) = day ? (214, 222, 232) : (160, 172, 192);
                _brush.Color = Color.FromArgb(A(13, o), r, gr, b);
                for (var i = 0; i < layer.Count; i++)
                {
                    ref var p = ref layer.Particles[i];
                    var w = p.Size;
                    var h = _height / layer.Count * 1.1f;
                    g.FillEllipse(_brush, (p.X - w * 0.5f) * u, (p.Y - h * 0.5f) * u, w * u, h * u);
                }

                break;
            }

            case WeatherEffectKind.SunGlints:
            case WeatherEffectKind.Stars:
            {
                var stars = layer.Effect.Kind == WeatherEffectKind.Stars;
                var (r, gr, b) = stars ? (214, 224, 255) : (255, 238, 196);
                _pen.Width = Math.Max(1f, 0.7f * u);
                for (var i = 0; i < layer.Count; i++)
                {
                    ref var p = ref layer.Particles[i];
                    var twinkle = 0.5f + 0.5f * MathF.Sin(_time * p.Rate * (stars ? 1.6f : 1.1f) + p.Phase);
                    var level = stars ? 0.25f + 0.75f * twinkle : twinkle * twinkle * twinkle;
                    if (level < 0.03f) continue;
                    var color = Color.FromArgb(A(PeakAlpha * level, o), r, gr, b);
                    var s = p.Size * u;
                    _brush.Color = color;
                    g.FillEllipse(_brush, p.X * u - s * 0.5f, p.Y * u - s * 0.5f, s, s);
                    if (!stars || p.Size > 1.3f)
                    {
                        // Four short arms that start outside the dot, so no pixel is covered twice: the glint of the sun on a surface.
                        var gap = s * 0.5f + 0.8f * u;
                        var arm = gap + (stars ? 2.2f : 3.5f) * u * level;
                        _pen.Color = color;
                        g.DrawLine(_pen, p.X * u - arm, p.Y * u, p.X * u - gap, p.Y * u);
                        g.DrawLine(_pen, p.X * u + gap, p.Y * u, p.X * u + arm, p.Y * u);
                        g.DrawLine(_pen, p.X * u, p.Y * u - arm, p.X * u, p.Y * u - gap);
                        g.DrawLine(_pen, p.X * u, p.Y * u + gap, p.X * u, p.Y * u + arm);
                    }
                }

                break;
            }
        }
    }

    public void Dispose()
    {
        _brush.Dispose();
        _pen.Dispose();
    }
}
