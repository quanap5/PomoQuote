namespace PomoQuote.Widget;

/// <summary>A date in the Vietnamese lunar calendar (Âm lịch).</summary>
/// <param name="Day">1 to 30.</param>
/// <param name="Month">1 to 12.</param>
/// <param name="Year">The lunar year (the year in which that lunar year starts).</param>
/// <param name="IsLeapMonth">The month is the leap month (tháng nhuận) of its number.</param>
internal readonly record struct LunarDate(int Day, int Month, int Year, bool IsLeapMonth)
{
    private static readonly string[] Can = ["Giáp", "Ất", "Bính", "Đinh", "Mậu", "Kỷ", "Canh", "Tân", "Nhâm", "Quý"];
    private static readonly string[] Chi = ["Tý", "Sửu", "Dần", "Mão", "Thìn", "Tỵ", "Ngọ", "Mùi", "Thân", "Dậu", "Tuất", "Hợi"];

    /// <summary>The year's name from the ten heavenly stems and the twelve earthly branches: 2025 is "Ất Tỵ", 2026 is "Bính Ngọ".</summary>
    public string YearName => $"{Can[((Year + 6) % 10 + 10) % 10]} {Chi[((Year + 8) % 12 + 12) % 12]}";

    /// <summary>"15/10 Bính Ngọ", or "1/6 nhuận Ất Tỵ" in a leap month.</summary>
    public string Text => $"{Day}/{Month}{(IsLeapMonth ? " nhuận" : "")} {YearName}";
}

/// <summary>
/// The Vietnamese lunar calendar for a solar date. It is the algorithm of Hồ Ngọc Đức (Astronomical Algorithms by Jean Meeus:
/// the moment of each new moon and the longitude of the sun, with the time zone fixed to UTC+7, which is what makes it the
/// Vietnamese and not the Chinese calendar: the two differ on the days where a new moon or a solar term falls near midnight at UTC+7
/// or UTC+8). It needs no network and no data tables, and is valid for the years 1200 to 2199 that the algorithm is documented for.
/// The solar date is the user's local calendar date; only the lunar rules use UTC+7.
/// </summary>
internal static class VietnameseLunarCalendar
{
    /// <summary>Vietnam's standard time, the time zone of the new moons.</summary>
    public const int TimeZone = 7;

    private const double SynodicMonth = 29.530588853;

    public static LunarDate FromSolar(DateOnly date) => FromSolar(date.Day, date.Month, date.Year);

    public static LunarDate FromSolar(int day, int month, int year)
    {
        var dayNumber = JulianDayNumber(day, month, year);
        var k = (int)Math.Floor((dayNumber - 2415021.076998695) / SynodicMonth);
        // The estimate of k can be one off near a new moon (the mean month is not the real one): settle it against the real new moons,
        // so that the month start is the last new moon on or before the day. (Without this a few days, such as 5 July 2054, came out as day 0.)
        while (NewMoonDay(k + 1) <= dayNumber) k++;
        while (NewMoonDay(k) > dayNumber) k--;
        var monthStart = NewMoonDay(k);

        var a11 = LunarMonth11(year);
        var b11 = a11;
        int lunarYear;
        if (a11 >= monthStart)
        {
            lunarYear = year;
            a11 = LunarMonth11(year - 1);
        }
        else
        {
            lunarYear = year + 1;
            b11 = LunarMonth11(year + 1);
        }

        var lunarDay = dayNumber - monthStart + 1;
        var diff = (int)Math.Floor((monthStart - a11) / 29.0);
        var leap = false;
        var lunarMonth = diff + 11;
        if (b11 - a11 > 365)
        {
            var leapMonthDiff = LeapMonthOffset(a11);
            if (diff >= leapMonthDiff)
            {
                lunarMonth = diff + 10;
                if (diff == leapMonthDiff) leap = true;
            }
        }

        if (lunarMonth > 12) lunarMonth -= 12;
        if (lunarMonth >= 11 && diff < 4) lunarYear -= 1;
        return new LunarDate(lunarDay, lunarMonth, lunarYear, leap);
    }

    /// <summary>The Julian day number of a (Gregorian, or Julian before 15 October 1582) calendar date.</summary>
    internal static int JulianDayNumber(int day, int month, int year)
    {
        var a = (14 - month) / 12;
        var y = year + 4800 - a;
        var m = month + 12 * a - 3;
        var jd = day + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
        if (jd < 2299161) jd = day + (153 * m + 2) / 5 + 365 * y + y / 4 - 32083;
        return jd;
    }

    /// <summary>The day number (at UTC+7) of the k-th new moon since 1900-01-01.</summary>
    internal static int NewMoonDay(int k)
    {
        var t = k / 1236.85;
        var t2 = t * t;
        var t3 = t2 * t;
        const double dr = Math.PI / 180;
        var jd1 = 2415020.75933 + SynodicMonth * k + 0.0001178 * t2 - 0.000000155 * t3;
        jd1 += 0.00033 * Math.Sin((166.56 + 132.87 * t - 0.009173 * t2) * dr);
        var m = 359.2242 + 29.10535608 * k - 0.0000333 * t2 - 0.00000347 * t3;
        var mpr = 306.0253 + 385.81691806 * k + 0.0107306 * t2 + 0.00001236 * t3;
        var f = 21.2964 + 390.67050646 * k - 0.0016528 * t2 - 0.00000239 * t3;
        var c1 = (0.1734 - 0.000393 * t) * Math.Sin(m * dr) + 0.0021 * Math.Sin(2 * dr * m);
        c1 = c1 - 0.4068 * Math.Sin(mpr * dr) + 0.0161 * Math.Sin(dr * 2 * mpr);
        c1 -= 0.0004 * Math.Sin(dr * 3 * mpr);
        c1 += 0.0104 * Math.Sin(dr * 2 * f) - 0.0051 * Math.Sin(dr * (m + mpr));
        c1 = c1 - 0.0074 * Math.Sin(dr * (m - mpr)) + 0.0004 * Math.Sin(dr * (2 * f + m));
        c1 = c1 - 0.0004 * Math.Sin(dr * (2 * f - m)) - 0.0006 * Math.Sin(dr * (2 * f + mpr));
        c1 += 0.0010 * Math.Sin(dr * (2 * f - mpr)) + 0.0005 * Math.Sin(dr * (2 * mpr + m));
        var deltaT = t < -11
            ? 0.001 + 0.000839 * t + 0.0002261 * t2 - 0.00000845 * t3 - 0.000000081 * t * t3
            : -0.000278 + 0.000265 * t + 0.000262 * t2;
        var jdNew = jd1 + c1 - deltaT;
        return (int)Math.Floor(jdNew + 0.5 + TimeZone / 24.0);
    }

    /// <summary>The longitude of the sun at the start of a day, in 30-degree steps (0 to 11): which of the twelve solar terms the day is in.</summary>
    internal static int SunLongitude(double dayNumber)
    {
        var t = (dayNumber - 2451545.5 - TimeZone / 24.0) / 36525;
        var t2 = t * t;
        const double dr = Math.PI / 180;
        var m = 357.52910 + 35999.05030 * t - 0.0001559 * t2 - 0.00000048 * t * t2;
        var l0 = 280.46645 + 36000.76983 * t + 0.0003032 * t2;
        var dl = (1.914600 - 0.004817 * t - 0.000014 * t2) * Math.Sin(dr * m);
        dl += (0.019993 - 0.000101 * t) * Math.Sin(dr * 2 * m) + 0.000290 * Math.Sin(dr * 3 * m);
        var l = (l0 + dl) * dr;
        l -= Math.PI * 2 * Math.Floor(l / (Math.PI * 2));
        return (int)Math.Floor(l / Math.PI * 6);
    }

    /// <summary>The day number of the new moon that starts the lunar 11th month (the one that contains the winter solstice) of a year.</summary>
    internal static int LunarMonth11(int year)
    {
        var offset = JulianDayNumber(31, 12, year) - 2415021;
        var k = (int)Math.Floor(offset / SynodicMonth);
        var newMoon = NewMoonDay(k);
        if (SunLongitude(newMoon) >= 9) newMoon = NewMoonDay(k - 1);
        return newMoon;
    }

    /// <summary>In a year with 13 lunar months: how many months after the 11th month the leap month is.</summary>
    internal static int LeapMonthOffset(int a11)
    {
        var k = (int)Math.Floor((a11 - 2415021.076998695) / SynodicMonth + 0.5);
        var i = 1;
        var arc = SunLongitude(NewMoonDay(k + i));
        int last;
        do
        {
            last = arc;
            i++;
            arc = SunLongitude(NewMoonDay(k + i));
        }
        while (arc != last && i < 14);
        return i - 1;
    }
}

/// <summary>The two lines shown after the clock: the solar date in Vietnamese and the lunar date.</summary>
internal static class DateLines
{
    private static readonly string[] Weekdays = ["Chủ Nhật", "Thứ Hai", "Thứ Ba", "Thứ Tư", "Thứ Năm", "Thứ Sáu", "Thứ Bảy"];

    /// <summary>"Thứ Ba, 24/11/2026".</summary>
    public static string Solar(DateOnly date) => $"{Weekdays[(int)date.DayOfWeek]}, {date.Day}/{date.Month}/{date.Year}";

    /// <summary>"Âm lịch: 15/10 Bính Ngọ".</summary>
    public static string Lunar(DateOnly date) => "Âm lịch: " + VietnameseLunarCalendar.FromSolar(date).Text;
}

/// <summary>
/// Remembers the two date lines of the current day, so nothing is computed or allocated while the clock ticks; they are
/// worked out again only when the local date changes (midnight, or when the PC wakes up on another day).
/// </summary>
internal sealed class DateLineCache
{
    private DateOnly _date;
    private string _solar = "";
    private string _lunar = "";
    private bool _has;

    public int Computations { get; private set; }

    /// <summary>The lines for <paramref name="today"/>; returns true when they changed (the widget redraws).</summary>
    public bool Update(DateOnly today, out string solar, out string lunar)
    {
        var changed = !_has || today != _date;
        if (changed)
        {
            _date = today;
            _solar = DateLines.Solar(today);
            _lunar = DateLines.Lunar(today);
            _has = true;
            Computations++;
        }

        solar = _solar;
        lunar = _lunar;
        return changed;
    }
}
