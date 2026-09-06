using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.SignalR;

namespace AutoCare.Services;

public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "user-" + userId);
        await base.OnConnectedAsync();
    }
}

public class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
{
    public async Task NotifyUserAsync(int userId, string title, string message, string category = "General")
    {
        var notification = new Notification
        {
            UserId = userId,
            Title = title,
            Message = message,
            Category = category
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        await hub.Clients.Group("user-" + userId).SendAsync("notificationReceived", new
        {
            notification.Id,
            notification.Title,
            notification.Message,
            notification.Category,
            CreatedAt = notification.CreatedAt.ToString("dd MMM yyyy HH:mm")
        });
    }
}
