using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Grafik
{
    public class ShiftEntry
    {
        public string Employees { get; set; }
        public DateTime Date { get; set; }
        public string Shift { get; set; }
        public string Worktime { get; set; }
        public bool IsSecondLine { get; set; } = false;
        public List<string> OtherEmployeesWithSameShift { get; set; } = new();
        public string DisplayOtherEmployees { get; set; }
        public string SecondLinePartner { get; set; }
    }

}
