using ClosedXML.Excel;
using CommunityToolkit.Maui.Storage;
using GrafikAdmin.Models;

namespace GrafikAdmin.Services;


/// Сервис экспорта расписания в Excel (формат совместимый с Grafik)

public class ExcelExportService
{
    public ExcelExportService()
    {
    }


    // Экспортировать в Excel с выбором пути сохранения

    public async Task<string> ExportToExcelAsync(MonthlySchedule schedule, CancellationToken cancellationToken = default)
    {
        // Получаем название месяца для предложения имени файла
        string monthName = GetRussianMonthName(schedule.Month);
        string suggestedFileName = $"{monthName}_{schedule.Year}.xlsx";

        // Создаем MemoryStream с файлом
        using var memoryStream = new MemoryStream();

        // Создаем Excel файл в памяти
        await Task.Run(() =>
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Расписание");

            var daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);

            // === СТРОКА 1: "Дата" + даты (каждая дата занимает 2 колонки) ===
            worksheet.Cell(1, 1).Value = "Дата";
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.LightGray;

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(schedule.Year, schedule.Month, day);
                int col = 2 + (day - 1) * 2;

                worksheet.Cell(1, col).Value = date.ToString("dd.MM.yyyy");
                worksheet.Range(1, col, 1, col + 1).Merge();
                worksheet.Cell(1, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(1, col).Style.Font.Bold = true;
                worksheet.Cell(1, col).Style.Fill.BackgroundColor = XLColor.LightGray;
            }

            // === СТРОКА 2: Дни недели ===
            worksheet.Cell(2, 1).Value = "";
            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(schedule.Year, schedule.Month, day);
                int col = 2 + (day - 1) * 2;

                string dayOfWeek = date.ToString("dddd", new System.Globalization.CultureInfo("ru-RU"));
                worksheet.Cell(2, col).Value = dayOfWeek;
                worksheet.Range(2, col, 2, col + 1).Merge();
                worksheet.Cell(2, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                {
                    worksheet.Cell(2, col).Style.Fill.BackgroundColor = XLColor.LightPink;
                }
            }

            // === СТРОКА 3: "сотрудник/смена" + "день/ночь" ===
            worksheet.Cell(3, 1).Value = "сотрудник \\ смена";
            worksheet.Cell(3, 1).Style.Fill.BackgroundColor = XLColor.LightGray;

            for (int day = 1; day <= daysInMonth; day++)
            {
                int col = 2 + (day - 1) * 2;
                worksheet.Cell(3, col).Value = "день";
                worksheet.Cell(3, col + 1).Value = "ночь";
                worksheet.Cell(3, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Cell(3, col + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // === Сотрудники первой линии ===
            var firstLineEmployees = schedule.Employees
                .Where(e => !schedule.SecondLineEmployees.Contains(e))
                .ToList();

            int currentRow = 4;

            foreach (var employee in firstLineEmployees)
            {
                worksheet.Cell(currentRow, 1).Value = employee;
                FillEmployeeRow(worksheet, currentRow, employee, schedule, daysInMonth);
                currentRow++;
            }

            currentRow += 2;

            // === Вторая линия ===
            if (schedule.SecondLineEmployees.Count > 0 || HasSecondLineShifts(schedule, firstLineEmployees))
            {
                int secondLineHeaderRow = currentRow;

                worksheet.Cell(currentRow, 1).Value = "дата";
                worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightGray;

                for (int day = 1; day <= daysInMonth; day++)
                {
                    var date = new DateTime(schedule.Year, schedule.Month, day);
                    int col = 2 + (day - 1) * 2;

                    worksheet.Cell(currentRow, col).Value = date.ToString("dd.MM.yyyy");
                    worksheet.Range(currentRow, col, currentRow, col + 1).Merge();
                    worksheet.Cell(currentRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                currentRow++;

                worksheet.Cell(currentRow, 1).Value = "время";
                worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.LightGray;

                for (int day = 1; day <= daysInMonth; day++)
                {
                    int col = 2 + (day - 1) * 2;
                    worksheet.Cell(currentRow, col).Value = "9-21";
                    worksheet.Cell(currentRow, col + 1).Value = "21-9";
                    worksheet.Cell(currentRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    worksheet.Cell(currentRow, col + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                currentRow++;

                // Сотрудники второй линии (штатные)
                foreach (var employee in schedule.SecondLineEmployees)
                {
                    worksheet.Cell(currentRow, 1).Value = employee;
                    FillEmployeeRow(worksheet, currentRow, employee, schedule, daysInMonth);
                    currentRow++;
                }

                // Сотрудники первой линии, назначенные на "2 линия" — добавляем их имена
                // в ячейки второй линии
                FillFirstLineAsSecondLine(worksheet, currentRow, firstLineEmployees, schedule, daysInMonth);
            }

            // === Форматирование ===
            worksheet.Column(1).Width = 22;
            for (int col = 2; col <= 2 + daysInMonth * 2; col++)
                worksheet.Column(col).Width = 6;

            var usedRange = worksheet.RangeUsed();
            if (usedRange != null)
            {
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            // Сохраняем в MemoryStream
            workbook.SaveAs(memoryStream);
            memoryStream.Position = 0; // Сбрасываем позицию для чтения
        });

        // Используем FileSaver из CommunityToolkit для сохранения с диалогом
        try
        {
            var fileSaverResult = await FileSaver.Default.SaveAsync(
                suggestedFileName,
                memoryStream,
                cancellationToken);

            fileSaverResult.EnsureSuccess();

            System.Diagnostics.Debug.WriteLine($"[ExcelExport] Файл сохранён: {fileSaverResult.FilePath}");
            return fileSaverResult.FilePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelExport] Ошибка при сохранении: {ex.Message}");

            // Fallback: сохраняем в папку приложения
            string fallbackPath = GetDefaultSavePath();
            string fileName = $"{GetRussianMonthName(schedule.Month)}_{schedule.Year}.xlsx";
            string filePath = Path.Combine(fallbackPath, fileName);

            // Сохраняем файл
            using var fileStream = File.Create(filePath);
            memoryStream.Position = 0;
            await memoryStream.CopyToAsync(fileStream);

            return filePath;
        }
    }


    // Получить название месяца на русском языке

    private string GetRussianMonthName(int month)
    {
        string[] monthNames =
        {
            "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь",
            "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
        };

        return monthNames[month - 1];
    }


    // Получить путь для сохранения по умолчанию

    private string GetDefaultSavePath()
    {
        try
        {
            string documentsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GrafikAdmin",
                "Расписания");

            if (!Directory.Exists(documentsPath))
                Directory.CreateDirectory(documentsPath);

            return documentsPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExcelExport] Ошибка при создании папки: {ex.Message}");
            return FileSystem.AppDataDirectory;
        }
    }


    // Проверяет, есть ли у сотрудников первой линии смены типа "2 линия"

    private static bool HasSecondLineShifts(MonthlySchedule schedule, List<string> firstLineEmployees)
    {
        return schedule.Entries.Any(e =>
            firstLineEmployees.Contains(e.EmployeeName) &&
            e.ShiftType.IsSecondLineShift());
    }


    // Заполнить строку сотрудника

    private static void FillEmployeeRow(IXLWorksheet worksheet, int row, string employee,
        MonthlySchedule schedule, int daysInMonth)
    {
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(schedule.Year, schedule.Month, day);
            int col = 2 + (day - 1) * 2;

            var entry = schedule.Entries.FirstOrDefault(e =>
                e.EmployeeName == employee && e.Date.Date == date.Date);

            if (entry != null)
            {
                switch (entry.ShiftType)
                {
                    case ShiftType.Day:
                        worksheet.Cell(row, col).Value = 12;
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEB3B");
                        break;

                    case ShiftType.Night:
                        worksheet.Cell(row, col + 1).Value = 12;
                        worksheet.Cell(row, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#90CAF9");
                        break;

                    // Сотрудник первой линии на "2 линия (день)" —
                    // в своей строке первой линии отображается как дневная смена
                    case ShiftType.SecondLineDay:
                        worksheet.Cell(row, col).Value = 12;
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEB3B");
                        break;

                    // Сотрудник первой линии на "2 линия (ночь)" —
                    // в своей строке первой линии отображается как ночная смена
                    case ShiftType.SecondLineNight:
                        worksheet.Cell(row, col + 1).Value = 12;
                        worksheet.Cell(row, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#90CAF9");
                        break;

                    case ShiftType.Vacation:
                        worksheet.Cell(row, col).Value = "отпуск";
                        worksheet.Range(row, col, row, col + 1).Merge();
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.LightGreen;
                        worksheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        break;

                    case ShiftType.SickLeave:
                        worksheet.Cell(row, col).Value = "б/л";
                        worksheet.Range(row, col, row, col + 1).Merge();
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.LightPink;
                        worksheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        break;

                    case ShiftType.DayOff:
                        var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                        if (isWeekend)
                        {
                            worksheet.Cell(row, col).Style.Fill.PatternType = XLFillPatternValues.LightUp;
                            worksheet.Cell(row, col + 1).Style.Fill.PatternType = XLFillPatternValues.LightUp;
                        }
                        break;
                }
            }
        }
    }


    // Вписать имена сотрудников первой линии, назначенных на "2 линия",
    // в область второй линии Excel (как на фото — "Павел" внизу во второй линии)

    private static void FillFirstLineAsSecondLine(IXLWorksheet worksheet, int startRow,
        List<string> firstLineEmployees, MonthlySchedule schedule, int daysInMonth)
    {
        // Собираем все записи первой линии с типом SecondLineDay/SecondLineNight
        var secondLineEntries = schedule.Entries
            .Where(e => firstLineEmployees.Contains(e.EmployeeName) && e.ShiftType.IsSecondLineShift())
            .ToList();

        if (secondLineEntries.Count == 0)
            return;

        // Группируем по дате + тип (день/ночь), чтобы знать, сколько строк нужно
        // На одну дату может быть несколько сотрудников первой линии на 2 линии
        // Находим максимальное количество сотрудников на одну дату+смену
        int maxPerSlot = 1;
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(schedule.Year, schedule.Month, day);
            var dayCount = secondLineEntries.Count(e => e.Date.Date == date.Date && e.ShiftType == ShiftType.SecondLineDay);
            var nightCount = secondLineEntries.Count(e => e.Date.Date == date.Date && e.ShiftType == ShiftType.SecondLineNight);
            maxPerSlot = Math.Max(maxPerSlot, Math.Max(dayCount, nightCount));
        }

        // Добавляем строки для имён сотрудников первой линии на второй линии
        for (int slot = 0; slot < maxPerSlot; slot++)
        {
            int row = startRow + slot;

            // Пустое имя — это дополнительная строка для имён
            worksheet.Cell(row, 1).Value = "";

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(schedule.Year, schedule.Month, day);
                int col = 2 + (day - 1) * 2;

                // Дневная смена "2 линия"
                var dayEntries = secondLineEntries
                    .Where(e => e.Date.Date == date.Date && e.ShiftType == ShiftType.SecondLineDay)
                    .ToList();

                if (slot < dayEntries.Count)
                {
                    var shortName = GetShortName(dayEntries[slot].EmployeeName);
                    worksheet.Cell(row, col).Value = shortName;
                    worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#C8E6C9");
                    worksheet.Cell(row, col).Style.Font.FontSize = 8;
                    worksheet.Cell(row, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                // Ночная смена "2 линия"
                var nightEntries = secondLineEntries
                    .Where(e => e.Date.Date == date.Date && e.ShiftType == ShiftType.SecondLineNight)
                    .ToList();

                if (slot < nightEntries.Count)
                {
                    var shortName = GetShortName(nightEntries[slot].EmployeeName);
                    worksheet.Cell(row, col + 1).Value = shortName;
                    worksheet.Cell(row, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#C8E6C9");
                    worksheet.Cell(row, col + 1).Style.Font.FontSize = 8;
                    worksheet.Cell(row, col + 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
            }
        }
    }


    // Извлечь короткое имя из ФИО (например, "Икалов Павел" → "Павел")

    private static string GetShortName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : parts[0];
    }
}


/// Информация об экспортированном файле

public record ExportedFile
{
    public string FileName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public long FileSize { get; init; }

    public string FileSizeDisplay => FileSize switch
    {
        < 1024 => $"{FileSize} B",
        < 1024 * 1024 => $"{FileSize / 1024.0:F1} KB",
        _ => $"{FileSize / (1024.0 * 1024):F1} MB"
    };
}