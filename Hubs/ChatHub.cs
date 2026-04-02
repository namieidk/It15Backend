using Microsoft.AspNetCore.SignalR;
using YourProject.Models;

namespace YourProject.Hubs
{
    public interface IChatClient
    {
        // React listener: connection.on("ReceiveMessage", (msg) => { ... })
        Task ReceiveMessage(Message message);
    }

    public class ChatHub : Hub<IChatClient>
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.GetHttpContext()?.Request.Query["userId"];
            if (!string.IsNullOrEmpty(userId))
            {
                // Always put the user in their own private room for 1-on-1s
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
            }
            await base.OnConnectedAsync();
        }

        // Logic to join a specific Group Chat room
        public async Task JoinGroup(string groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, groupId);
        }

        public async Task LeaveGroup(string groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupId);
        }
    }
}