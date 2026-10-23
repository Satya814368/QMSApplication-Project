using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Data;

public class LanguageTaskInput
{
    public string Language { get; set; } = "";
    public string Targets { get; set; } = "";
    public long AssignedUserId { get; set; }
}

public static class LanguageTaskFactory
{
    public static async Task<(List<DeliverableTask> Tasks, string? Error)> BuildAsync(
        QMSPortalDbContext db, string? json, string username)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 100_000)
            return (new(), "Add at least one language task (maximum 100 KB of task details).");
        List<LanguageTaskInput>? rows;
        try
        {
            rows = JsonSerializer.Deserialize<List<LanguageTaskInput>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 4 });
        }
        catch (JsonException) { return (new(), "Task details are invalid. Please review the language tasks."); }
        if (rows == null || rows.Count is < 1 or > 40 || rows.Any(x => x == null))
            return (new(), "Add between 1 and 40 language tasks.");

        var activeIds = await db.PortalUsers.AsNoTracking().Where(x => x.IsActive)
            .Select(x => x.Id).ToListAsync();
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<DeliverableTask>();
        var totalTargets = 0;
        foreach (var row in rows)
        {
            var language = row.Language?.Trim() ?? "";
            if (language.Length is < 1 or > 80 || !languages.Add(language))
                return (new(), "Each language must be unique and contain 1–80 characters.");
            if (!activeIds.Contains(row.AssignedUserId))
                return (new(), $"Choose an active registered member for {language}.");
            var numbers = Regex.Split(row.Targets?.Trim() ?? "", @"[,;\s]+").Where(x => x.Length > 0).ToList();
            totalTargets += numbers.Count;
            if (numbers.Count is < 1 or > 200 || totalTargets > 400)
                return (new(), "Each language needs 1–200 targets; use at most 400 targets per submission.");
            if (numbers.Any(x => !Regex.IsMatch(x, @"^[0-9]{1,40}$")) || numbers.Distinct().Count() != numbers.Count)
                return (new(), $"Enter unique numeric target numbers for {language}, separated by commas or spaces.");
            var task = new DeliverableTask
            {
                Language = language, AssignedUserId = row.AssignedUserId,
                CreatedAtUtc = DateTime.UtcNow, CreatedBy = username
            };
            foreach (var number in numbers)
                task.Targets.Add(new DeliverableTarget
                {
                    TargetNumber = number, AssignedUserId = row.AssignedUserId,
                    UpdatedAtUtc = DateTime.UtcNow, UpdatedBy = username,
                    Steps = Enumerable.Range(1, TaskWorkflow.Steps.Count)
                        .Select(i => new TargetProgressStep { StepNumber = i }).ToList()
                });
            tasks.Add(task);
        }
        return (tasks, null);
    }
}
