using DataBuilder.Core.Entities;

namespace DataBuilder.Api.Models;

public class ProjectListViewModel
{
    public List<Project> Projects { get; set; } = new();
}

public class ProjectCreateViewModel
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class ProjectDetailViewModel
{
    public Project Project { get; set; } = null!;
    public List<Document> Documents { get; set; } = new();
}

public class ProjectSettingsViewModel
{
    public Project Project { get; set; } = null!;
}
