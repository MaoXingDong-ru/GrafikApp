using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Grafik.Services;

public sealed record NextShiftInfo(ShiftEntry Entry, DateTime Start, DateTime End, bool IsActive);

public sealed record MonthStats(int Total, int Day, int Night, int DaysOff);

public static class ScheduleInsights
{
    private static readonly CultureInfo Ru = new("ru-RU");
    private static readonly Regex TimeRegex = new(@"(\d{1,2}):(\d{2})", RegexOptions.Compiled);

    /// <summary>Начало и конец смены по Worktime (учитывает ночные и «сдвоенные» смены).</summary>
    public static bool TryGetRange(ShiftEntry entry, out DateTime start, out DateTime end)
    {
        start = end = default;

        if (string.IsNullOrWhiteSpace(entry.Shift) || string.IsNullOrWhiteSpace(entry.Worktime))
            return false;

        var matches = TimeRegex.Matches(entry.Worktime);
        if (matches.Count == 0)
            return false;

        if (!TryToTime(matches[0], out var startTime) || !TryToTime(matches[^1], out var endTime))
            return false;

        start = entry.Date.Date + startTime;
        end = entry.Date.Date + endTime;

        if (matches.Count == 1 || end <= start)
            end = matches.Count == 1 ? start.AddHours(12) : end.AddDays(1);

        return true;
    }

    private static bool TryToTime(Match m, out TimeSpan time)
    {
        time = default;
        if (!int.TryParse(m.Groups[1].Value, out var h) || !int.TryParse(m.Groups[2].Value, out var min))
            return false;
        if (h is < 0 or > 23 || min is < 0 or > 59)
            return false;

        time = new TimeSpan(h, min, 0);
        return true;
    }

    /// <summary>Идущая сейчас или ближайшая смена сотрудника (по всем слотам).</summary>
    public static NextShiftInfo? GetNextShift(IEnumerable<ShiftEntry> all, string employee, DateTime now)
    {
        NextShiftInfo? best = null;

        foreach (var entry in all.Where(e => string.Equals(e.Employees, employee, StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryGetRange(entry, out var start, out var end) || end <= now)
                continue;

            if (best == null || start < best.Start)
                best = new NextShiftInfo(entry, start, end, start <= now);
        }

        return best;
    }

    public static string DayLabel(DateTime date, DateTime today) =>
        (date.Date - today.Date).Days switch
        {
            0 => "Сегодня",
            1 => "Завтра",
            _ => date.ToString("d MMMM, ddd", Ru)
        };

    public static string Countdown(NextShiftInfo shift, DateTime now) =>
        shift.IsActive
            ? $"Идёт сейчас · до конца {FormatSpan(shift.End - now)}"
            : $"Начало через {FormatSpan(shift.Start - now)}";

    private static string FormatSpan(TimeSpan span)
    {
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays} д {span.Hours} ч";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} ч {span.Minutes} мин";
        return $"{Math.Max(1, (int)span.TotalMinutes)} мин";
    }

    public static MonthStats GetMonthStats(IEnumerable<ShiftEntry> employeeMonth, ScheduleMonth month)
    {
        var work = employeeMonth.Where(e => !string.IsNullOrWhiteSpace(e.Shift)).ToList();

        int day = work.Count(e => e.Shift.Contains("днев", StringComparison.OrdinalIgnoreCase));
        int night = work.Count(e => e.Shift.Contains("ноч", StringComparison.OrdinalIgnoreCase));
        int workDays = work.Select(e => e.Date.Date).Distinct().Count();
        int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);

        return new MonthStats(workDays, day, night, daysInMonth - workDays);
    }

    public static string FormatStats(MonthStats s) =>
        $"Смен: {s.Total} · дневных {s.Day} · ночных {s.Night} · выходных {s.DaysOff}";

    /// <summary>Кто работает в указанный день (многострочный текст для карточки).</summary>
    public static string DescribeWorkingOn(IEnumerable<ShiftEntry> all, DateTime date, string? me)
    {
        var groups = all
            .Where(s => s.Date.Date == date.Date && !string.IsNullOrWhiteSpace(s.Shift))
            .GroupBy(s => s.Shift)
            .OrderBy(g => g.Key)
            .ToList();

        if (groups.Count == 0)
            return string.Empty;

        string Name(string n) =>
            !string.IsNullOrEmpty(me) && string.Equals(n, me, StringComparison.OrdinalIgnoreCase) ? $"★ {n}" : n;

        var sb = new StringBuilder();

        foreach (var g in groups)
        {
            var worktime = g.First().Worktime;
            sb.AppendLine(string.IsNullOrWhiteSpace(worktime) ? g.Key : $"{g.Key} ({worktime})");

            var first = g.Where(e => !e.IsSecondLine).Select(e => Name(e.Employees)).ToList();
            var second = g.Where(e => e.IsSecondLine).Select(e => Name(e.Employees)).ToList();

            if (first.Count > 0) sb.AppendLine($"   {string.Join(", ", first)}");
            if (second.Count > 0) sb.AppendLine($"   2-я линия: {string.Join(", ", second)}");
        }

        return sb.ToString().TrimEnd();
    }
}