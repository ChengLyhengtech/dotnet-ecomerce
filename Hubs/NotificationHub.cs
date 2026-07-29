using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ecomerce_dotnet.Hubs
{
    [Authorize(Roles = "Admin,Manager")]
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // Automatically add connected Admin/Manager users to the "Admins" broadcast group
            await Groups.AddToGroupAsync(Context.ConnectionId, "Admins");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(System.Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Admins");
            await base.OnDisconnectedAsync(exception);
        }
    }
}
