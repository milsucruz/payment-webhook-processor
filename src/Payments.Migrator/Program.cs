using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Payments.Infrastructure.Persistence;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Payments")));

using IHost host = builder.Build();

using IServiceScope scope = host.Services.CreateScope();
PaymentsDbContext dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
await dbContext.Database.MigrateAsync();
