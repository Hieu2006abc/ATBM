using System;
using System.Collections.Generic;

namespace BTL_2.Models;

public partial class Company
{
    public int CompanyId { get; set; }

    public string CompanyName { get; set; } = null!;

    public string? Logo { get; set; }

    public string? Address { get; set; }

    public string? Description { get; set; }

    public string? Website { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public DateTime? CreatedDate { get; set; }

    public int? EmployerId { get; set; }

    // THÊM PROPERTY NÀY - không có trong database, chỉ dùng để hiển thị
    public int JobCount { get; set; }

    public virtual User? Employer { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}