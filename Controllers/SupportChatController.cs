using System.Security.Claims;
using AutoCare.Data;
using AutoCare.Models;
using AutoCare.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AutoCare.Controllers;

[Authorize]
public class SupportChatController(AppDbContext db, IHubContext<NotificationHub> hub) : Controller
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool IsAdmin => User.IsInRole("Admin");

    public async Task<IActionResult> Index()
    {
        if (IsAdmin) return RedirectToAction(nameof(Admin));

        var thread = await db.ChatThreads
            .Include(x => x.User)
            .Include(x => x.Messages).ThenInclude(x => x.SenderUser)
            .FirstOrDefaultAsync(x => x.UserId == CurrentUserId);

        if (thread == null)
        {
            thread = new ChatThread
            {
                UserId = CurrentUserId,
                Subject = "Workshop support",
                UserLastReadAt = DateTime.Now
            };
            db.ChatThreads.Add(thread);
            await db.SaveChangesAsync();
            thread = await LoadThreadAsync(thread.Id);
        }
        else
        {
            thread.UserLastReadAt = DateTime.Now;
            await db.SaveChangesAsync();
        }

        return View("Chat", new ChatPageVm { SelectedThread = thread, IsAdmin = false });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin(int? id = null)
    {
        var threads = await db.ChatThreads
            .Include(x => x.User)
            .Include(x => x.Messages)
            .OrderByDescending(x => x.LastMessageAt)
            .ToListAsync();

        ChatThread? selected = null;
        if (id.HasValue)
        {
            selected = await LoadThreadAsync(id.Value);
            if (selected != null)
            {
                selected.AdminLastReadAt = DateTime.Now;
                await db.SaveChangesAsync();
            }
        }
        else if (threads.Count > 0)
        {
            return RedirectToAction(nameof(Admin), new { id = threads[0].Id });
        }

        return View("Chat", new ChatPageVm { Threads = threads, SelectedThread = selected, IsAdmin = true });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(int? threadId, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { error = "Message is required." });

        ChatThread? thread = IsAdmin
            ? await db.ChatThreads.Include(x => x.User).FirstOrDefaultAsync(x => x.Id == threadId)
            : await db.ChatThreads.Include(x => x.User).FirstOrDefaultAsync(x => x.UserId == CurrentUserId);

        if (thread == null)
        {
            if (IsAdmin) return NotFound();
            thread = new ChatThread { UserId = CurrentUserId, Subject = "Workshop support" };
            db.ChatThreads.Add(thread);
            await db.SaveChangesAsync();
        }

        if (!IsAdmin && thread.UserId != CurrentUserId) return Forbid();
        if (thread.IsClosed)
            return BadRequest(new { error = "This conversation is closed." });

        string cleanMessage = message.Trim();
        if (cleanMessage.Length > 1000) cleanMessage = cleanMessage[..1000];

        var chatMessage = new ChatMessage
        {
            ChatThreadId = thread.Id,
            SenderUserId = CurrentUserId,
            SenderRole = IsAdmin ? ChatSenderRole.Admin : ChatSenderRole.User,
            Message = cleanMessage,
            SentAt = DateTime.Now
        };
        db.ChatMessages.Add(chatMessage);
        thread.LastMessageAt = chatMessage.SentAt;
        if (IsAdmin) thread.AdminLastReadAt = chatMessage.SentAt;
        else thread.UserLastReadAt = chatMessage.SentAt;
        await db.SaveChangesAsync();

        var payload = new
        {
            messageId = chatMessage.Id,
            threadId = thread.Id,
            senderName = User.Identity?.Name ?? (IsAdmin ? "Admin" : "Customer"),
            senderRole = chatMessage.SenderRole.ToString(),
            message = chatMessage.Message,
            sentAt = chatMessage.SentAt.ToString("dd MMM yyyy HH:mm")
        };

        await hub.Clients.Group("chat-" + thread.Id).SendAsync("chatMessageReceived", payload);
        if (IsAdmin)
            await hub.Clients.Group("user-" + thread.UserId).SendAsync("chatConversationUpdated", payload);
        else
            await hub.Clients.Group("admins").SendAsync("chatConversationUpdated", payload);

        return Json(payload);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleClosed(int id)
    {
        var thread = await db.ChatThreads.FindAsync(id);
        if (thread == null) return NotFound();
        thread.IsClosed = !thread.IsClosed;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Admin), new { id });
    }

    private Task<ChatThread?> LoadThreadAsync(int id) =>
        db.ChatThreads
            .Include(x => x.User)
            .Include(x => x.Messages.OrderBy(m => m.SentAt)).ThenInclude(x => x.SenderUser)
            .FirstOrDefaultAsync(x => x.Id == id);
}
