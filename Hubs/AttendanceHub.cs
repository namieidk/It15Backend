using Microsoft.AspNetCore.SignalR;

namespace YourProject.Hubs
{
    public class AttendanceHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            
            // Extract metadata from the connection request
            string department = httpContext?.Request.Query["department"].ToString() ?? "";
            string role = httpContext?.Request.Query["role"].ToString()?.ToUpper() ?? "EMPLOYEE";

            // 1. HR gets access to the Global Broadcast Group
            if (role == "HR")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "HR_GLOBAL");
                Console.WriteLine($"[SignalR] HR Admin {Context.ConnectionId} joined HR_GLOBAL");
            }

            if (!string.IsNullOrEmpty(department))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, department);
                Console.WriteLine($"[SignalR] User {Context.ConnectionId} ({role}) joined group: {department}");
            }

            await base.OnConnectedAsync();
        }

        public async Task JoinDepartmentGroup(string department)
        {
            if (!string.IsNullOrEmpty(department))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, department);
                Console.WriteLine($"[SignalR] Connection {Context.ConnectionId} manually joined: {department}");
            }
        }
        public async Task LeaveDepartmentGroup(string department)
        {
            if (!string.IsNullOrEmpty(department))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, department);
                Console.WriteLine($"[SignalR] Connection {Context.ConnectionId} left group: {department}");
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            Console.WriteLine($"[SignalR] Connection {Context.ConnectionId} terminated.");
            await base.OnDisconnectedAsync(exception);
        }
    }
}