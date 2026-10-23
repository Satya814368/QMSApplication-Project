using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QMSApplication.Models;

namespace QMSApplication.Pages
{
public class NotificationEditModel : PageModel
{
private readonly QMSPortalDbContext _db;

public NotificationEditModel(QMSPortalDbContext db)
{
_db = db;
}

[BindProperty]
public long Id { get; set; }

[BindProperty]
public string Title { get; set; } = string.Empty;

[BindProperty]
public string Message { get; set; } = string.Empty;

[BindProperty]
public string Priority { get; set; } = "Normal";

[BindProperty]
public bool IsMandatory { get; set; }

[BindProperty]
public bool IsPublished { get; set; }

[BindProperty]
public string? TargetRole { get; set; }

private bool IsAdminOrManager()
{
var role = HttpContext.Session.GetString("UserRole");

return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
|| string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase);
}

public async Task<IActionResult>
OnGetAsync(long id)
{
if (!IsAdminOrManager())
{
return RedirectToPage("/Notifications");
}

var notification = await _db.Notifications
.FirstOrDefaultAsync(x => x.Id == id);

if (notification == null)
{
return NotFound();
}

Id = notification.Id;
Title = notification.Title;
Message = notification.Message;
Priority = notification.Priority;
IsMandatory = notification.IsMandatory;
IsPublished = notification.IsPublished;
TargetRole = notification.TargetRole;

return Page();
}

public async Task<IActionResult>
OnPostAsync()
{
if (!IsAdminOrManager())
{
return RedirectToPage("/Notifications");
}

if (string.IsNullOrWhiteSpace(Title))
{
ModelState.AddModelError(
nameof(Title),
"Title is required.");
}

if (string.IsNullOrWhiteSpace(Message))
{
ModelState.AddModelError(
nameof(Message),
"Message is required.");
}

if (!ModelState.IsValid)
{
return Page();
}

var notification = await _db.Notifications
.FirstOrDefaultAsync(x => x.Id == Id);

if (notification == null)
{
return NotFound();
}

notification.Title = Title.Trim();
notification.Message = Message.Trim();
notification.Priority = Priority;
notification.IsMandatory = IsMandatory;
notification.IsPublished = IsPublished;
notification.TargetRole = string.IsNullOrWhiteSpace(TargetRole)
? null
: TargetRole;

notification.UpdatedAtUtc = DateTime.UtcNow;

await _db.SaveChangesAsync();

return RedirectToPage("/NotificationAdmin");
}
}
}