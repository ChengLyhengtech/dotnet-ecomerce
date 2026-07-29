using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ecomerce_dotnet.Data;
using Ecomerce_dotnet.Models;

namespace Ecomerce_dotnet.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    //[Authorize(Roles = "Admin,Manager")]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(ApplicationDbContext context, ILogger<NotificationsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/Notifications/unread
        [HttpGet("unread")]
        public async Task<IActionResult> GetUnreadNotifications()
        {
            try
            {
                var unreadCount = await _context.Notifications
                    .Where(n => !n.IsRead)
                    .CountAsync();

                var latestNotifications = await _context.Notifications
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(10)
                    .Select(n => new
                    {
                        n.Id,
                        n.Title,
                        n.Message,
                        n.Type,
                        n.TargetUrl,
                        n.IsRead,
                        n.CreatedAt
                    })
                    .ToListAsync();

                return Ok(new
                {
                    unreadCount,
                    notifications = latestNotifications
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch unread notifications.");
                return StatusCode(500, new { message = "An error occurred while fetching notifications.", error = ex.Message });
            }
        }

        // PUT: api/Notifications/{id}/read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            try
            {
                var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id);
                if (notification == null)
                {
                    return NotFound(new { message = "Notification record not found." });
                }

                if (!notification.IsRead)
                {
                    notification.IsRead = true;
                    await _context.SaveChangesAsync();
                }

                return Ok(new { message = "Notification marked as read successfully.", id = notification.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark notification {Id} as read.", id);
                return StatusCode(500, new { message = "An error occurred while updating notification status.", error = ex.Message });
            }
        }

        // PUT: api/Notifications/read-all
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            try
            {
                var unreadNotifications = await _context.Notifications
                    .Where(n => !n.IsRead)
                    .ToListAsync();

                if (unreadNotifications.Any())
                {
                    foreach (var notification in unreadNotifications)
                    {
                        notification.IsRead = true;
                    }

                    await _context.SaveChangesAsync();
                }

                return Ok(new { message = "All unread notifications marked as read successfully.", count = unreadNotifications.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to mark all notifications as read.");
                return StatusCode(500, new { message = "An error occurred while updating all notifications.", error = ex.Message });
            }
        }
    }
}
