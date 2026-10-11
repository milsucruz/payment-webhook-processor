var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapControllers();

// Liveness only: no checks registered, so it answers as long as the process responds (architecture section 9).
app.MapHealthChecks("/health/live");

app.Run();
