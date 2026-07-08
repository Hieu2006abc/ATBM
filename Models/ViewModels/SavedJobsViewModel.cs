using System;
using System.Collections.Generic;

namespace BTL_2.Models.ViewModels
{
    public class SavedJobsViewModel
    {
        public int SavedJobId { get; set; }
        public int JobId { get; set; }
        public string JobTitle { get; set; }
        public string CompanyName { get; set; }
        public string CompanyLogo { get; set; }
        public string Location { get; set; }
        public string JobType { get; set; }
        public decimal? SalaryMin { get; set; }
        public decimal? SalaryMax { get; set; }
        public DateTime? Deadline { get; set; }
        public DateTime SavedDate { get; set; }
        public bool IsActive { get; set; }
    }
}