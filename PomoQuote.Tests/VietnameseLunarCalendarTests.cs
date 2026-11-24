using PomoQuote.Widget;

namespace PomoQuote.Tests;

/// <summary>
/// The lunar date is checked against dates that are published as Vietnamese public holidays and festivals (the government's
/// holiday announcements for Tết, Giỗ Tổ Hùng Vương on 10/3, Tết Trung Thu on 15/8, Tết Đoan Ngọ on 5/5, ông Táo on 23/12), the
/// known leap months (2020 tháng 4, 2023 tháng 2, 2025 tháng 6) and the algorithm's own invariants over many years.
/// </summary>
public sealed class VietnameseLunarCalendarTests
{
    private static LunarDate Lunar(int year, int month, int day) => VietnameseLunarCalendar.FromSolar(day, month, year);

    // ---------- Tết Nguyên Đán: 1/1 of the new lunar year ----------

    [Theory]
    [InlineData(2018, 2, 16, "Mậu Tuất")]
    [InlineData(2019, 2, 5, "Kỷ Hợi")]
    [InlineData(2020, 1, 25, "Canh Tý")]
    [InlineData(2021, 2, 12, "Tân Sửu")]
    [InlineData(2022, 2, 1, "Nhâm Dần")]
    [InlineData(2023, 1, 22, "Quý Mão")]
    [InlineData(2024, 2, 10, "Giáp Thìn")]
    [InlineData(2025, 1, 29, "Ất Tỵ")]
    [InlineData(2026, 2, 17, "Bính Ngọ")]
    [InlineData(2027, 2, 6, "Đinh Mùi")]
    [InlineData(2028, 1, 26, "Mậu Thân")]
    [InlineData(2029, 2, 13, "Kỷ Dậu")]
    public void Tet_is_the_first_day_of_the_first_month(int year, int month, int day, string yearName)
    {
        var date = Lunar(year, month, day);

        Assert.Equal(1, date.Day);
        Assert.Equal(1, date.Month);
        Assert.False(date.IsLeapMonth);
        Assert.Equal(year, date.Year);
        Assert.Equal(yearName, date.YearName);
    }

    [Theory]
    [InlineData(2025, 1, 28, 29, 12, 2024)] // the day before Tết 2025: the last day of Giáp Thìn
    [InlineData(2025, 1, 22, 23, 12, 2024)] // ông Táo
    [InlineData(2024, 12, 31, 1, 12, 2024)] // new moon at 05:27 on 31 December at UTC+7
    [InlineData(2025, 1, 1, 2, 12, 2024)] // 1 January is still in the old lunar year
    [InlineData(2025, 12, 31, 12, 11, 2025)] // 31 December is in the 11th month of Ất Tỵ
    [InlineData(2026, 1, 1, 13, 11, 2025)]
    [InlineData(2025, 2, 12, 15, 1, 2025)] // rằm tháng Giêng
    public void Year_boundaries_stay_in_the_right_lunar_month_and_year(int y, int m, int d, int lunarDay, int lunarMonth, int lunarYear)
    {
        var date = Lunar(y, m, d);

        Assert.Equal((lunarDay, lunarMonth, lunarYear, false), (date.Day, date.Month, date.Year, date.IsLeapMonth));
    }

    // ---------- festivals on fixed lunar days ----------

    [Theory]
    [InlineData(2022, 4, 10, 3, 10)] // Giỗ Tổ Hùng Vương 2022 (10/3)
    [InlineData(2023, 4, 29, 3, 10)]
    [InlineData(2024, 4, 18, 3, 10)]
    [InlineData(2025, 4, 7, 3, 10)]
    [InlineData(2026, 4, 26, 3, 10)]
    [InlineData(2025, 5, 31, 5, 5)] // Tết Đoan Ngọ
    [InlineData(2022, 9, 10, 8, 15)] // Tết Trung Thu
    [InlineData(2023, 9, 29, 8, 15)]
    [InlineData(2024, 9, 17, 8, 15)]
    [InlineData(2025, 10, 6, 8, 15)]
    [InlineData(2026, 9, 25, 8, 15)]
    [InlineData(2025, 9, 6, 7, 15)] // Vu Lan, after the leap 6th month
    public void Public_festivals_fall_on_their_lunar_days(int y, int m, int d, int lunarMonth, int lunarDay)
    {
        var date = Lunar(y, m, d);

        Assert.Equal((lunarDay, lunarMonth, false), (date.Day, date.Month, date.IsLeapMonth));
    }

    // ---------- leap months ----------

    [Theory]
    [InlineData(2020, 5, 22, 30, 4, false)] // the day before the leap month: end of the ordinary 4th month
    [InlineData(2020, 5, 23, 1, 4, true)] // nhuận tháng 4 Canh Tý
    [InlineData(2020, 6, 20, 29, 4, true)]
    [InlineData(2020, 6, 21, 1, 5, false)]
    [InlineData(2023, 2, 20, 1, 2, false)]
    [InlineData(2023, 3, 22, 1, 2, true)] // nhuận tháng 2 Quý Mão
    [InlineData(2023, 4, 19, 29, 2, true)]
    [InlineData(2023, 4, 20, 1, 3, false)]
    [InlineData(2025, 7, 24, 30, 6, false)]
    [InlineData(2025, 7, 25, 1, 6, true)] // nhuận tháng 6 Ất Tỵ
    [InlineData(2025, 8, 22, 29, 6, true)]
    [InlineData(2025, 8, 23, 1, 7, false)]
    public void Leap_months_are_marked_and_their_neighbours_are_not(int y, int m, int d, int lunarDay, int lunarMonth, bool leap)
    {
        var date = Lunar(y, m, d);

        Assert.Equal((lunarDay, lunarMonth, leap), (date.Day, date.Month, date.IsLeapMonth));
    }

    [Fact]
    public void The_text_marks_the_leap_month_and_names_the_year()
    {
        Assert.Equal("1/6 nhuận Ất Tỵ", Lunar(2025, 7, 25).Text);
        Assert.Equal("1/1 Ất Tỵ", Lunar(2025, 1, 29).Text);
        Assert.Equal("15/8 Bính Ngọ", Lunar(2026, 9, 25).Text);
    }

    // ---------- the time zone is Vietnam's ----------

    [Fact]
    public void A_new_moon_just_before_midnight_at_UTC_plus_7_starts_the_month_on_that_day_as_in_Vietnam()
    {
        // The new moon of 29 January 1968 fell late in the evening at UTC+7: North Vietnam celebrated Tết that day, the South (UTC+8) a day later.
        var date = Lunar(1968, 1, 29);

        Assert.Equal((1, 1, 1968), (date.Day, date.Month, date.Year));
        Assert.Equal("Mậu Thân", date.YearName);
    }

    // ---------- the algorithm's invariants over many years ----------

    [Fact]
    public void Every_day_from_1950_to_2100_follows_the_one_before_with_months_of_29_or_30_days_and_valid_numbers()
    {
        var day = new DateOnly(1950, 1, 1);
        var previous = VietnameseLunarCalendar.FromSolar(day);
        var monthLengths = new List<int>();
        var leapMonths = 0;
        for (day = day.AddDays(1); day.Year < 2101; day = day.AddDays(1))
        {
            var current = VietnameseLunarCalendar.FromSolar(day);
            Assert.True(current.Day is >= 1 and <= 30, $"{day}: {current}");
            Assert.InRange(current.Month, 1, 12);
            if (current.Day == previous.Day + 1)
            {
                Assert.Equal((previous.Month, previous.Year, previous.IsLeapMonth), (current.Month, current.Year, current.IsLeapMonth));
            }
            else
            {
                Assert.Equal(1, current.Day);
                Assert.InRange(previous.Day, 29, 30);
                monthLengths.Add(previous.Day);
                if (current.IsLeapMonth) leapMonths++;
                // The month number goes on by one (or stays for a leap month), and the year changes only after the 12th month.
                var expectedMonth = current.IsLeapMonth ? previous.Month : previous.Month % 12 + 1;
                Assert.Equal(expectedMonth, current.Month);
                Assert.True(current.Year == previous.Year || (previous.Month == 12 && current.Year == previous.Year + 1), $"{day}: year {previous.Year} to {current.Year}");
            }

            previous = current;
        }

        Assert.All(monthLengths, length => Assert.InRange(length, 29, 30));
        Assert.InRange(leapMonths, 52, 60); // 7 leap months in every 19 years: about 55 in 150 years
    }

    [Fact]
    public void Every_lunar_year_from_2001_to_2060_starts_with_Tet_between_21_January_and_21_February_and_has_12_or_13_months()
    {
        for (var year = 2001; year <= 2060; year++)
        {
            var tet = Enumerable.Range(0, 60).Select(offset => new DateOnly(year, 1, 20).AddDays(offset)).First(d => VietnameseLunarCalendar.FromSolar(d) is { Day: 1, Month: 1 });
            Assert.True(tet >= new DateOnly(year, 1, 21) && tet <= new DateOnly(year, 2, 21), $"Tet {year} on {tet}");

            var next = Enumerable.Range(0, 60).Select(offset => new DateOnly(year + 1, 1, 20).AddDays(offset)).First(d => VietnameseLunarCalendar.FromSolar(d) is { Day: 1, Month: 1 });
            var days = next.DayNumber - tet.DayNumber;
            Assert.InRange(days, 353, 385); // 12 months of 29 or 30 days, or 13 with a leap month
        }
    }

    [Fact]
    public void The_year_name_follows_the_stem_and_branch_cycle()
    {
        Assert.Equal("Ất Tỵ", new LunarDate(1, 1, 2025, false).YearName);
        Assert.Equal("Bính Ngọ", new LunarDate(1, 1, 2026, false).YearName);
        Assert.Equal("Giáp Tý", new LunarDate(1, 1, 1984, false).YearName);
        Assert.Equal("Canh Tý", new LunarDate(1, 1, 2020, false).YearName);
        Assert.Equal("Giáp Tý", new LunarDate(1, 1, 2044, false).YearName); // the 60-year cycle
        Assert.Equal("Quý Hợi", new LunarDate(1, 1, 1983, false).YearName);
    }

    // ---------- the two lines ----------

    [Theory]
    [InlineData(2026, 11, 24, "Thứ Ba, 24/11/2026")]
    [InlineData(2026, 11, 22, "Chủ Nhật, 22/11/2026")]
    [InlineData(2025, 1, 29, "Thứ Tư, 29/1/2025")]
    [InlineData(2024, 2, 29, "Thứ Năm, 29/2/2024")]
    [InlineData(2026, 3, 6, "Thứ Sáu, 6/3/2026")]
    [InlineData(2026, 3, 7, "Thứ Bảy, 7/3/2026")]
    [InlineData(2026, 3, 9, "Thứ Hai, 9/3/2026")]
    public void The_solar_line_has_the_vietnamese_weekday_and_day_month_year(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, DateLines.Solar(new DateOnly(y, m, d)));
    }

    [Fact]
    public void The_lunar_line_says_Am_lich_and_the_lunar_date()
    {
        Assert.Equal("Âm lịch: 1/1 Bính Ngọ", DateLines.Lunar(new DateOnly(2026, 2, 17)));
        Assert.Equal("Âm lịch: 1/6 nhuận Ất Tỵ", DateLines.Lunar(new DateOnly(2025, 7, 25)));
    }

    [Fact]
    public void The_date_lines_are_worked_out_once_a_day_and_again_at_midnight()
    {
        var cache = new DateLineCache();
        var day = new DateOnly(2026, 11, 24);

        Assert.True(cache.Update(day, out var solar, out var lunar));
        for (var tick = 0; tick < 5000; tick++) Assert.False(cache.Update(day, out _, out _)); // the clock ticks all day
        Assert.Equal(1, cache.Computations);
        Assert.Equal("Thứ Ba, 24/11/2026", solar);
        Assert.StartsWith("Âm lịch: ", lunar);

        Assert.True(cache.Update(day.AddDays(1), out var nextSolar, out _)); // midnight, or a wake from sleep on another day
        Assert.Equal("Thứ Tư, 25/11/2026", nextSolar);
        Assert.Equal(2, cache.Computations);
    }
}
