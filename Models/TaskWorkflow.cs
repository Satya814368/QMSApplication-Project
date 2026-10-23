namespace QMSApplication.Models;

public static class TaskWorkflow
{
    public static readonly IReadOnlyList<string> Steps = Array.AsReadOnly(new[]
    {
        "Received Project",
        "Sent to AdHoc",
        "Received from AdHoc",
        "Comparison is in progress at i-ray",
        "Review is in progress at i-ray",
        "Preparation of Clarifications in word template",
        "Send Clarifications to GN",
        "Waiting for comments on Clarifications",
        "Preparation of UG's with corrections & Build",
        "Waiting for Confirmation on GN Review",
        "Changes incorporation w.r.t. GN Comments",
        "Sent to Agile",
        "Update ECO Number in Excel",
        "ECO approved"
    });

    public static bool CanManage(string? role) =>
        new[] { "Admin", "Manager", "Team Lead", "Team Lead (TL)", "TL" }
            .Contains(role, StringComparer.OrdinalIgnoreCase);

    // Cancelled targets retain their history and never become 100% complete.
    public static int Percent(DeliverableTarget target) => target.IsCancelled ? 0 :
        (int)Math.Round(target.Steps.Count(x => x.Status == "Completed") * 100m / Steps.Count);

    public static string CurrentStep(DeliverableTarget target)
    {
        if (target.IsCancelled) return "Cancelled with approval from GN";
        var step = target.Steps.OrderBy(x => x.StepNumber)
            .FirstOrDefault(x => x.Status == "In Progress")
            ?? target.Steps.OrderBy(x => x.StepNumber).FirstOrDefault(x => x.Status != "Completed");
        return step == null ? "All 14 steps completed" : $"({step.StepNumber}/14) {Steps[step.StepNumber - 1]}";
    }

    // Preserve the total exactly, including remainders: 61 minutes / 3 = 21,20,20.
    public static long[] SplitMinutes(long total, int count)
    {
        if (total < 0 || count < 1) throw new ArgumentOutOfRangeException();
        return Enumerable.Range(0, count).Select(i => total / count + (i < total % count ? 1 : 0)).ToArray();
    }

    public static string Duration(long minutes) => $"{minutes / 60}h {minutes % 60}m";
}
