using System;
using System.Collections.Generic;

namespace BTL_2.Models;

public partial class Category
{
    public int CategoryId { get; set; }

    public string CategoryName { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
