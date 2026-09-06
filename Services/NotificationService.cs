using AutoCare.Data;
using AutoCare.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AutoCare.Services;

public class NotificationHub(AppDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrWhiteSpace(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, "user-" + userId);
        if (Context.User?.IsInRole("Admin") == true)
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");
        await base.OnConnectedAsync();
    }

    public async Task JoinChatThread(int threadId)
    {
        int? userId = int.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : null;
        if (userId == null) return;
        bool canJoin = Context.User?.IsInRole("Admin") == true ||
            await db.ChatThreads.AnyAsync(x => x.Id == threadId && x.UserId == userId.Value);
        if (canJoin) await Groups.AddToGroupAsync(Context.ConnectionId, "chat-" + threadId);
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
