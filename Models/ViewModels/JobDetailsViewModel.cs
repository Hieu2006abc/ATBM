using System;
using System.Collections.Generic;

namespace BTL_2.Models.ViewModels
{
    public class JobDetailsViewModel
    {
        public Job Job { get; set; }
        public List<Job> RelatedJobs { get; set; }
        public bool HasApplied { get; set; }
    }
}