using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;

namespace Grafik
{
    public class ShiftEntry
    {
        // Основная информация
        public DateTime Date { get; set; }
        public string Shift { get; set; }      // Дневная, Ночная, Выходной и т.д.
        public string Worktime { get; set; }   // Время работы (например, 08:00-20:00)

        // Сотрудники
        public string Employees { get; set; }
        public bool IsSecondLine { get; set; } = false;
        public List<string> OtherEmployeesWithSameShift { get; set; } = new();
        public string DisplayOtherEmployees { get; set; }
        public string SecondLinePartner { get; set; }

        // Для календаря
        public Color TileColor { get; set; } = Colors.Transparent; // Цвет плитки по типу смены
        public Color BorderColor { get; set; } = Colors.Transparent; // Цвет обводки (сегодняшний день)

        // Видимость дня в календаре (false для пустых ячеек в начале месяца)
        public bool IsVisibleDay { get; set; } = true;
    }
}
