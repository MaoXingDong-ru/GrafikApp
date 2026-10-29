using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Grafik.Services;

public readonly record struct ScheduleMonth(int Year, int Month) : IComparable<ScheduleMonth>
{
    public int CompareTo(ScheduleMonth other) => (Year * 12 + Month).CompareTo(other.Year * 12 + other.Month);

    public string Key => $"{Year:D4}-{Month:D2}";

    public string MonthName
    {
        get
        {
            var ru = new CultureInfo("ru-RU");
            return ru.TextInfo.ToTitleCase(ru.DateTimeFormat.GetMonthName(Month));
        }
    }

    public string Title => $"{MonthName} {Year}";

    public bool Contains(DateTime d) => d.Year == Year && d.Month == Month;
}

/// <summary>Что произойдёт при загрузке: какой месяц добавится и какой будет удалён.</summary>
public sealed record ScheduleLoadPlan(ScheduleMonth NewMonth, ScheduleMonth? Evicted, bool ReplacesSame);

/// <summary>Хранилище расписаний: максимум 3 слота, по одному на месяц.</summary>
public static class ScheduleSlotService
{
    public const int MaxSlots = 3;

    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly Regex SlotFile = new(@"^schedule_(\d{4})-(\d{2})\.json$", RegexOptions.Compiled);

    private static string Dir => FileSystem.AppDataDirectory;
    private static string SlotPath(ScheduleMonth m) => Path.Combine(Dir, $"schedule_{m.Key}.json");
    private static string MergedPath => Path.Combine(Dir, "schedule.json");

    public static ScheduleMonth? DetectMonth(List<ShiftEntry> schedule)
    {
        var top = schedule
            .Where(s => s.Date != DateTime.MinValue)
            .GroupBy(s => new ScheduleMonth(s.Date.Year, s.Date.Month))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return top?.Key;
    }

    /// <summary>Слоты в порядке возрастания месяцев.</summary>
    public static List<ScheduleMonth> GetSlots()
    {
        lock (Sync)
        {
            MigrateLegacyIfNeeded();
            return ScanSlots();
        }
    }

    public static ScheduleLoadPlan? Inspect(List<ShiftEntry> schedule)
    {
        var month = DetectMonth(schedule);
        if (month == null) return null;

        var slots = GetSlots();
        if (slots.Contains(month.Value))
            return new ScheduleLoadPlan(month.Value, null, true);

        ScheduleMonth? evicted = slots.Count >= MaxSlots ? slots[0] : null;
        return new ScheduleLoadPlan(month.Value, evicted, false);
    }

    public static ScheduleLoadPlan Save(List<ShiftEntry> schedule)
    {
        var plan = Inspect(schedule)
            ?? throw new InvalidOperationException("Не удалось определить месяц расписания.");

        var entries = schedule.Where(s => plan.NewMonth.Contains(s.Date)).ToList();

        lock (Sync)
        {
            File.WriteAllText(SlotPath(plan.NewMonth), JsonSerializer.Serialize(entries, JsonOpts));

            if (plan.Evicted is ScheduleMonth old && File.Exists(SlotPath(old)))
                File.Delete(SlotPath(old));

            RebuildMergedFile();
        }

        return plan;
    }

    public static List<ShiftEntry> LoadSlot(ScheduleMonth month)
    {
        try
        {
            var path = SlotPath(month);
            return File.Exists(path)
                ? JsonSerializer.Deserialize<List<ShiftEntry>>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    public static List<ShiftEntry> LoadAll()
    {
        lock (Sync)
        {
            return GetSlots().SelectMany(LoadSlot).ToList();
        }
    }

    public static List<string> GetEmployees() =>
        LoadAll().Select(s => s.Employees).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();

    public static void ClearAll()
    {
        lock (Sync)
        {
            foreach (var m in ScanSlots())
                File.Delete(SlotPath(m));

            if (File.Exists(MergedPath))
                File.Delete(MergedPath);
        }
    }

    // ---- внутреннее ----

    private static List<ScheduleMonth> ScanSlots()
    {
        var result = new List<ScheduleMonth>();
        foreach (var file in Directory.EnumerateFiles(Dir, "schedule_*.json"))
        {
            var match = SlotFile.Match(Path.GetFileName(file));
            if (match.Success)
                result.Add(new ScheduleMonth(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)));
        }
        result.Sort();
        return result;
    }

    // schedule.json — объединение всех слотов (для остальных частей приложения)
    private static void RebuildMergedFile()
    {
        var all = ScanSlots().SelectMany(LoadSlot).ToList();
        File.WriteAllText(MergedPath, JsonSerializer.Serialize(all, JsonOpts));
    }

    // Старый формат: один schedule.json -> разбиваем на слоты
    private static void MigrateLegacyIfNeeded()
    {
        if (ScanSlots().Count > 0 || !File.Exists(MergedPath))
            return;

        try
        {
            var legacy = JsonSerializer.Deserialize<List<ShiftEntry>>(File.ReadAllText(MergedPath)) ?? new();

            var groups = legacy
                .Where(s => s.Date != DateTime.MinValue)
                .GroupBy(s => new ScheduleMonth(s.Date.Year, s.Date.Month))
                .OrderBy(g => g.Key)
                .TakeLast(MaxSlots);

            foreach (var g in groups)
                File.WriteAllText(SlotPath(g.Key), JsonSerializer.Serialize(g.ToList(), JsonOpts));

            RebuildMergedFile();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScheduleSlotService] Ошибка миграции: {ex.Message}");
        }
    }
}