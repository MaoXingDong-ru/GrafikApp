using ClosedXML.Excel;
using GrafikAdmin.Models;

namespace GrafikAdmin.Services;

/// <summary>
/// Сервис экспорта расписания в Excel (формат совместимый с Grafik)
/// </summary>
public class ExcelExportService
{
    private readonly string _shareDirectory;

    public ExcelExportService()
    {
        _shareDirectory = Path.Combine(FileSystem.AppDataDirectory, "share");
        
        if (!Directory.Exists(_shareDirectory))
            Directory.CreateDirectory(_shareDirectory);
    }

    public string ShareDirectory => _shareDirectory;

    /// <summary>
    /// Экспортировать в Excel
    /// </summary>
    public async Task<string> ExportToExcelAsync(MonthlySchedule schedule)
    {
        return await Task.Run(() =>
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
                
                // Выходные выделяем цветом
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

            // === Сотрудники первой линии (начиная со строки 4) ===
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

            // === Пустые строки-разделители ===
            currentRow += 2;

            // === Вторая линия (если есть) ===
            if (schedule.SecondLineEmployees.Count > 0)
            {
                // Повторяем заголовки дат для второй линии
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

                // Строка времени
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

                // Сотрудники второй линии
                foreach (var employee in schedule.SecondLineEmployees)
                {
                    worksheet.Cell(currentRow, 1).Value = employee;
                    FillEmployeeRow(worksheet, currentRow, employee, schedule, daysInMonth);
                    currentRow++;
                }
            }

            // === Форматирование ===
            worksheet.Column(1).Width = 22;
            for (int col = 2; col <= 2 + daysInMonth * 2; col++)
                worksheet.Column(col).Width = 6;

            // Границы для использованного диапазона
            var usedRange = worksheet.RangeUsed();
            if (usedRange != null)
            {
                usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            }

            // === Сохранение ===
            var fileName = $"Расписание_{schedule.Year}_{schedule.Month:D2}.xlsx";
            var filePath = Path.Combine(_shareDirectory, fileName);
            workbook.SaveAs(filePath);

            System.Diagnostics.Debug.WriteLine($"[ExcelExport] Файл сохранён: {filePath}");

            return filePath;
        });
    }

    /// <summary>
    /// Заполнить строку сотрудника
    /// </summary>
    private static void FillEmployeeRow(IXLWorksheet worksheet, int row, string employee, 
        MonthlySchedule schedule, int daysInMonth)
    {
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(schedule.Year, schedule.Month, day);
            int col = 2 + (day - 1) * 2; // день = col, ночь = col + 1
            
            var entry = schedule.Entries.FirstOrDefault(e => 
                e.EmployeeName == employee && e.Date.Date == date.Date);

            if (entry != null)
            {
                switch (entry.ShiftType)
                {
                    case ShiftType.Day:
                        worksheet.Cell(row, col).Value = 12;
                        worksheet.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEB3B"); // Жёлтый
                        break;
                        
                    case ShiftType.Night:
                        worksheet.Cell(row, col + 1).Value = 12;
                        worksheet.Cell(row, col + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#90CAF9"); // Голубой
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
                        // Выходной — ячейки пустые, можно добавить штриховку
                        var isWeekend = date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;
                        if (isWeekend)
                        {
                            // Штриховка для выходных
                            worksheet.Cell(row, col).Style.Fill.PatternType = XLFillPatternValues.LightUp;
                            worksheet.Cell(row, col + 1).Style.Fill.PatternType = XLFillPatternValues.LightUp;
                        }
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Получить список экспортированных файлов
    /// </summary>
    public List<ExportedFile> GetExportedFiles()
    {
        var files = new List<ExportedFile>();

        if (!Directory.Exists(_shareDirectory))
            return files;

        foreach (var file in Directory.GetFiles(_shareDirectory, "*.xlsx"))
        {
            var fileInfo = new FileInfo(file);
            files.Add(new ExportedFile
            {
                FileName = fileInfo.Name,
                FilePath = file,
                CreatedAt = fileInfo.CreationTime,
                FileSize = fileInfo.Length
            });
        }

        return files.OrderByDescending(f => f.CreatedAt).ToList();
    }

    /// <summary>
    /// Очистить папку share
    /// </summary>
    public void ClearShareDirectory()
    {
        if (Directory.Exists(_shareDirectory))
        {
            foreach (var file in Directory.GetFiles(_shareDirectory))
                File.Delete(file);
        }
    }
}

/// <summary>
/// Информация об экспортированном файле
/// </summary>
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