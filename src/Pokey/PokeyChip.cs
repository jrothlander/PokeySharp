using System;

namespace Pokey;

/// <summary>
/// A POKEY (C012294) audio section, emulated at the base clock.
///
/// <b>Scope: the sound half only.</b> The chip also does keyboard scan, paddle
/// pots, serial IO and timers; none of that is here, and on the hardware this
/// project cares about (an arcade cabinet, and the Atari 7800) most of it is not
/// even connected. What is here is the four audio channels, the three polynomial
/// counters, and every AUDCTL bit that changes what comes out of them.
///
/// <b>How the chip actually makes a sound.</b> Each channel has an 8-bit counter
/// reloaded from its AUDF register, counting down at a clock the channel selects.
/// On underflow the channel may toggle a one-bit output flip-flop -- <i>may</i>,
/// because AUDC's distortion field can gate that toggle behind a polynomial
/// counter's current bit. So the divider sets the rate and the poly decides whether
/// each opportunity is taken. Toggling every time gives a square wave; toggling only
/// when a poly bit is high gives that poly's texture, pitched by the divider. The
/// channel's volume is added to the mix while its flip-flop is high.
///
/// That single mechanism accounts for every sound a POKEY can make, which is why
/// this class is mostly bookkeeping around <see cref="Tick"/>.
/// </summary>
public sealed class PokeyChip
{
    public const int Channels = 4;

    // --- Register addresses ------------------------------------------------

    public const int AUDF1 = 0x00, AUDC1 = 0x01;
    public const int AUDF2 = 0x02, AUDC2 = 0x03;
    public const int AUDF3 = 0x04, AUDC3 = 0x05;
    public const int AUDF4 = 0x06, AUDC4 = 0x07;
    public const int AUDCTL = 0x08;

    // --- AUDCTL bits -------------------------------------------------------

    /// <summary>Bit 0: the base clock is divided by 114 rather than 28, globally.</summary>
    public const int CtlFifteenKhz = 0x01;
    /// <summary>
    /// Bit 1: channel 1 is high-pass filtered, clocked by channel 3.
    ///
    /// <b>These two were the wrong way round until checked against a reference.</b> The
    /// filtering logic was always right -- channel 1 really was held by channel 3 and
    /// channel 2 by channel 4 -- but the two AUDCTL bits that switch them on were
    /// swapped, so writing $02 enabled the channel-2 filter and $04 the channel-1 one.
    /// Nothing in the sound would tell you: both filters exist, both work, and a program
    /// asking for one quietly got the other.
    ///
    /// Corrected against the C012294 documentation at
    /// <c>7800.8bitdev.org/index.php/POKEY_C012294_Documentation</c>, which gives bit 1
    /// as the channel-1 filter clocked by channel 3 and bit 2 as the channel-2 filter
    /// clocked by channel 4.
    /// </summary>
    public const int CtlFilter1By3 = 0x02;

    /// <summary>Bit 2: channel 2 is high-pass filtered, clocked by channel 4. See
    /// <see cref="CtlFilter1By3"/> for why these two swapped.</summary>
    public const int CtlFilter2By4 = 0x04;
    /// <summary>Bit 3: channels 3 and 4 join into one 16-bit counter.</summary>
    public const int CtlJoin34 = 0x08;
    /// <summary>Bit 4: channels 1 and 2 join into one 16-bit counter.</summary>
    public const int CtlJoin12 = 0x10;
    /// <summary>Bit 5: channel 3 runs directly off the base clock.</summary>
    public const int CtlCh3Fast = 0x20;
    /// <summary>Bit 6: channel 1 runs directly off the base clock.</summary>
    public const int CtlCh1Fast = 0x40;
    /// <summary>Bit 7: the 17-bit poly counter is shortened to 9 bits.</summary>
    public const int CtlNineBitPoly = 0x80;

    // --- AUDC fields -------------------------------------------------------

    /// <summary>Bits 0-3: volume, 0-15.</summary>
    public const int AudcVolumeMask = 0x0f;

    /// <summary>Bit 4: <b>volume-only mode</b> -- the divider and poly are cut out
    /// entirely and the volume value is driven straight at the output. This is how
    /// digitised speech and sampled sound were done on the Atari, by rewriting this
    /// nibble at the sample rate, and it is the one mode where the channel makes no
    /// sound of its own at all.</summary>
    public const int AudcVolumeOnly = 0x10;

    // --- State -------------------------------------------------------------

    private readonly byte[] _audf = new byte[Channels];
    private readonly byte[] _audc = new byte[Channels];
    private int _audctl;

    private readonly int[] _counter = new int[Channels];
    private readonly bool[] _output = new bool[Channels];

    /// <summary>High-pass sample-and-hold, one per filtered channel (1 and 2).</summary>
    private readonly bool[] _filterHold = new bool[2];

    private readonly PolyCounters _poly = new();

    /// <summary>Divider chain for the two slow clocks, run from the base clock.</summary>
    private int _slowCounter;

    private double _cycleRemainder;

    /// <summary>The clock the whole chip is driven at. Not a constant: the whole
    /// point of <see cref="PokeyClocks.TempestHz"/> is that this differs per
    /// machine and every pitch moves with it.</summary>
    public double BaseClockHz { get; set; } = PokeyClocks.Atari800NtscHz;

    public PokeyChip() => Reset();

    public void Reset()
    {
        Array.Clear(_audf);
        Array.Clear(_audc);
        Array.Clear(_counter);
        Array.Clear(_output);
        Array.Clear(_filterHold);
        _audctl = 0;
        _slowCounter = 0;
        _cycleRemainder = 0;
        _poly.Reset();
        for (int i = 0; i < Channels; i++) Reload(i);
    }

    // --- Register access ---------------------------------------------------

    public void Write(int register, int value)
    {
        value &= 0xff;
        switch (register)
        {
            case AUDF1: case AUDF2: case AUDF3: case AUDF4:
                _audf[register >> 1] = (byte)value;
                break;
            case AUDC1: case AUDC2: case AUDC3: case AUDC4:
                _audc[register >> 1] = (byte)value;
                break;
            case AUDCTL:
                _audctl = value;
                _poly.NineBitPoly = (value & CtlNineBitPoly) != 0;
                break;
        }
    }

    public int Audf(int channel) => _audf[channel];
    public int Audc(int channel) => _audc[channel];
    public int Control => _audctl;

    public void SetAudf(int channel, int value) => _audf[channel] = (byte)(value & 0xff);
    public void SetAudc(int channel, int value) => _audc[channel] = (byte)(value & 0xff);
    public void SetControl(int value) => Write(AUDCTL, value);

    // --- Derived, for a UI to display without duplicating the rules ---------

    /// <summary>Whether a channel is joined to the one below it as a 16-bit pair.
    /// Channel 2 is the high half of a 1+2 pair, channel 4 the high half of 3+4.</summary>
    public bool IsHighHalf(int channel)
        => (channel == 1 && (_audctl & CtlJoin12) != 0)
        || (channel == 3 && (_audctl & CtlJoin34) != 0);

    /// <summary>Whether a channel is the low half of a joined pair -- it then clocks
    /// its partner instead of driving the mix on its own.</summary>
    public bool IsLowHalf(int channel)
        => (channel == 0 && (_audctl & CtlJoin12) != 0)
        || (channel == 2 && (_audctl & CtlJoin34) != 0);

    /// <summary>Whether a channel takes the base clock directly rather than a slow
    /// one. Only channels 1 and 3 can.</summary>
    public bool IsFast(int channel)
        => (channel == 0 && (_audctl & CtlCh1Fast) != 0)
        || (channel == 2 && (_audctl & CtlCh3Fast) != 0);

    /// <summary>The clock feeding a channel's counter, in Hz. For the high half of a
    /// joined pair this is meaningless on its own -- it is clocked by its partner --
    /// so the pair's combined rate is reported instead.</summary>
    public double InputClockHz(int channel)
    {
        if (IsFast(channel)) return BaseClockHz;
        return (_audctl & CtlFifteenKhz) != 0
            ? PokeyClocks.VerySlow(BaseClockHz)
            : PokeyClocks.Slow(BaseClockHz);
    }

    /// <summary>
    /// The pitch a channel is currently producing, in Hz, for display.
    ///
    /// Follows the same rules the ticking does: a joined pair is one 16-bit divider,
    /// and a channel on the base clock reloads with a larger addend. Returns 0 for
    /// the low half of a pair, which produces no output of its own.
    /// </summary>
    public double PitchHz(int channel)
    {
        if (IsLowHalf(channel)) return 0.0;

        if (IsHighHalf(channel))
        {
            int low = channel - 1;
            int divider = _audf[low] | (_audf[channel] << 8);
            double input = IsFast(low) ? BaseClockHz : InputClockHz(low);
            int addend = IsFast(low) ? 7 : 1;
            return input / (2.0 * (divider + addend));
        }

        return PokeyClocks.Pitch(InputClockHz(channel), _audf[channel], IsFast(channel) ? 4 : 1);
    }

    /// <summary>The distortion field, AUDC bits 5-7. Six distinct behaviours across
    /// eight codes -- 1 and 3 are both plain 5-bit, 5 and 7 are both pure tone.</summary>
    public int Distortion(int channel) => (_audc[channel] >> 5) & 7;

    public int Volume(int channel) => _audc[channel] & AudcVolumeMask;
    public bool VolumeOnly(int channel) => (_audc[channel] & AudcVolumeOnly) != 0;

    /// <summary>A human-readable name for a distortion code, so a UI does not carry
    /// its own copy of the table.</summary>
    public static string DistortionName(int code) => code switch
    {
        0 => "5 + 17 BIT POLY",
        1 => "5 BIT POLY",
        2 => "5 + 4 BIT POLY",
        3 => "5 BIT POLY",
        4 => "17 BIT POLY",
        5 => "PURE TONE",
        6 => "4 BIT POLY",
        7 => "PURE TONE",
        _ => "?",
    };

    // --- The chip ----------------------------------------------------------

    /// <summary>
    /// Advances the whole chip by one base clock.
    ///
    /// Order matters. The polynomial counters run first and unconditionally, because
    /// they free-run at the base clock no matter what the channels are doing -- that
    /// shared timebase is why two channels on the same distortion stay related rather
    /// than drifting into independence. Then the slow clock divider, then the
    /// channels, low halves before high so a joined pair's carry lands in the same
    /// tick it was generated.
    /// </summary>
    public void Tick()
    {
        _poly.Tick();

        bool slowTick = false;
        int slowPeriod = (_audctl & CtlFifteenKhz) != 0
            ? PokeyClocks.VerySlowDivider
            : PokeyClocks.SlowDivider;

        if (++_slowCounter >= slowPeriod)
        {
            _slowCounter = 0;
            slowTick = true;
        }

        for (int channel = 0; channel < Channels; channel++)
        {
            // The high half of a joined pair is clocked by its partner's underflow,
            // handled there, not by a clock of its own.
            if (IsHighHalf(channel)) continue;

            bool clocked = IsFast(channel) || slowTick;
            if (clocked) Clock(channel);
        }
    }

    /// <summary>One input clock into a channel's counter. A joined pair is one
    /// counter, so its underflow fires the high half, which is what drives the mix.</summary>
    private void Clock(int channel)
    {
        if (--_counter[channel] > 0) return;

        Reload(channel);
        Fire(IsLowHalf(channel) ? channel + 1 : channel);
    }

    /// <summary>
    /// A counter has underflowed: take the toggle opportunity, or don't.
    ///
    /// This is the whole sound generator. The distortion field decides which
    /// polynomial bit -- if any -- has to be high for the output flip-flop to flip.
    /// Pure tone flips every time and gives a square wave at half the divider rate;
    /// everything else flips only sometimes, which is what turns a rate into a
    /// texture.
    /// </summary>
    private void Fire(int channel)
    {
        bool allow = Distortion(channel) switch
        {
            0 => _poly.Bit5 && _poly.BitLong,
            1 => _poly.Bit5,
            2 => _poly.Bit5 && _poly.Bit4,
            3 => _poly.Bit5,
            4 => _poly.BitLong,
            5 => true,
            6 => _poly.Bit4,
            7 => true,
            _ => true,
        };

        if (allow) _output[channel] = !_output[channel];

        // The high-pass filters are sample-and-hold: channel 3 firing latches
        // channel 1's current level, channel 4 latches channel 2's. The filtered
        // channel then outputs the XOR of its own level with the held one, which
        // cancels anything the two agree on and passes only what changed faster
        // than the clocking channel.
        if (channel == 2 && (_audctl & CtlFilter1By3) != 0) _filterHold[0] = _output[0];
        if (channel == 3 && (_audctl & CtlFilter2By4) != 0) _filterHold[1] = _output[1];
    }

    private void Reload(int channel)
    {
        // A joined pair is a single 16-bit counter, low byte first -- not two 8-bit
        // counters in series. That distinction is the whole point of joining: two in
        // series would give a period of (low+1)x(high+1), a product with huge gaps in
        // what it can express, where a real 16-bit counter reaches every value in
        // between. Getting this wrong still makes a sound, just never the one asked
        // for, which is how it would survive being listened to.
        if (IsLowHalf(channel))
        {
            int combined = _audf[channel] | (_audf[channel + 1] << 8);
            _counter[channel] = combined + (IsFast(channel) ? 7 : 1);
            return;
        }

        // The addend is the hardware's reload delay, not a fudge: 1 on the slow
        // clocks, 4 for a channel taking the base clock directly.
        _counter[channel] = _audf[channel] + (IsFast(channel) ? 4 : 1);
    }

    /// <summary>Whether a channel is currently driving the mix.</summary>
    private bool Level(int channel)
    {
        if (channel == 0 && (_audctl & CtlFilter1By3) != 0) return _output[0] ^ _filterHold[0];
        if (channel == 1 && (_audctl & CtlFilter2By4) != 0) return _output[1] ^ _filterHold[1];
        return _output[channel];
    }

    /// <summary>
    /// The chip's output right now, <b>unipolar, 0..1</b>.
    ///
    /// Each channel contributes its volume while it is high, or continuously in
    /// volume-only mode, or not at all if it is the low half of a joined pair. Four
    /// channels at volume 15 is the maximum, so that is what the sum is scaled by --
    /// a single channel therefore never approaches full scale, which is correct and
    /// is why POKEY music is quieter than a single test tone.
    ///
    /// <b>Positive-only on purpose.</b> The real chip drives current one way; there
    /// is no negative half. Centring it here would be wrong in a specific way that
    /// matters -- silence is sum 0, so mapping to -1..1 would put a full-scale DC
    /// offset on a silent chip, and turning a channel off would produce a click.
    /// <see cref="Render"/> removes the DC the way the hardware did, with a
    /// capacitor.
    /// </summary>
    public float Mix()
    {
        int sum = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            if (IsLowHalf(channel)) continue;
            int volume = Volume(channel);
            if (volume == 0) continue;

            if (VolumeOnly(channel)) sum += volume;
            else if (Level(channel)) sum += volume;
        }

        const int Maximum = Channels * AudcVolumeMask;
        return sum / (float)Maximum;
    }

    /// <summary>
    /// Renders <paramref name="buffer"/> at <paramref name="sampleRate"/>.
    ///
    /// <b>Box-filtered, not point-sampled.</b> The chip runs about forty times faster
    /// than any sane sample rate, so reading its level once per output sample would
    /// alias every square edge into the audio band and make high notes sound wrong in
    /// a way that is hard to diagnose. Averaging the level across all the base clocks
    /// that fall inside a sample is a one-line fix and most of what a proper
    /// resampler would buy.
    ///
    /// The fractional cycle carries across calls, so streaming in small blocks gives
    /// the same output as rendering one long one -- which the live front end depends
    /// on and which a test can assert.
    /// </summary>
    public void Render(float[] buffer, int offset, int count, double sampleRate)
    {
        double cyclesPerSample = BaseClockHz / sampleRate;

        for (int i = 0; i < count; i++)
        {
            _cycleRemainder += cyclesPerSample;
            int cycles = (int)_cycleRemainder;
            _cycleRemainder -= cycles;

            float sum = 0f;
            for (int c = 0; c < cycles; c++)
            {
                Tick();
                sum += Mix();
            }

            float level = cycles > 0 ? sum / cycles : Mix();

            // The capacitor. Mix() is positive-only, as the chip is, so without this
            // every sound sits on a DC step and starting or stopping one thumps.
            // A one-pole high-pass at a few Hz removes the offset and nothing
            // audible: the coefficient is set from the sample rate so the corner
            // stays put whatever rate the front end asks for.
            float coefficient = (float)(1.0 - 2.0 * Math.PI * DcBlockHz / sampleRate);
            _dcState = level + coefficient * _dcState - _dcPrevious;
            _dcPrevious = level;

            buffer[offset + i] = _dcState;
        }
    }

    /// <summary>Corner frequency of the DC blocker. Low enough to be inaudible, high
    /// enough to settle within a few milliseconds of a sound starting.</summary>
    private const double DcBlockHz = 8.0;

    private float _dcState, _dcPrevious;
}
