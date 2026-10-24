using Microsoft.AspNetCore.SignalR;

namespace Payments.Api.Hubs;

// Details: docs/code/PaymentsHub.md
public sealed class PaymentsHub : Hub
{
    public const string Route = "/hubs/payments";

    private readonly HubConnectionCounter connectionCounter;

    public PaymentsHub(HubConnectionCounter connectionCounter)
    {
        this.connectionCounter = connectionCounter;
    }

    public override Task OnConnectedAsync()
    {
        this.connectionCounter.Increment();
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        this.connectionCounter.Decrement();
        return base.OnDisconnectedAsync(exception);
    }
}
