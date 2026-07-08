using System.Collections.Generic;

namespace BTL_2.Models.ViewModels
{
    public class ProfileViewModel
    {
        public User User { get; set; }
        public List<Application> Applications { get; set; }
        public List<Job> AllJobs { get; set; }
        public List<Company> FollowedCompanies { get; set; }
        public string ActiveTab { get; set; } = "info";
    }
}