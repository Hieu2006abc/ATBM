// Models/ViewModels/TestResultViewModel.cs
using System;

namespace BTL_2.Models.ViewModels
{
    public class TestResultViewModel
    {
        public string TestName { get; set; }
        public string ExpectedResult { get; set; }
        public string ActualResult { get; set; }
        public bool Passed { get; set; }
        public DateTime TestTime { get; set; } = DateTime.Now;
    }
}