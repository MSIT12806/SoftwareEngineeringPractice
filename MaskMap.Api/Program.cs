using MaskMap.Api;
using MaskMap.Api.Domains;
using MaskMap.Api.Infrastructure.Reservations;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is required.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IDb>(provider => provider.GetRequiredService<AppDbContext>());
var capacityStrategy = builder.Configuration["ReservationCapacity:Strategy"] ?? "Cas";
if (capacityStrategy.Equals("Cas", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<
        IReservationCapacityClaimer,
        AtomicUpdateReservationCapacityClaimer>();
}
else if (capacityStrategy.Equals("Updlock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<
        IReservationCapacityClaimer,
        UpdlockReservationCapacityClaimer>();
}
else if (capacityStrategy.Equals("Aggregate", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<
        IReservationCapacityClaimer,
        AggregateReservationCapacityClaimer>();
}
else
{
    throw new InvalidOperationException(
        $"Unknown ReservationCapacity:Strategy '{capacityStrategy}'. " +
        "Use 'Cas', 'Updlock', or 'Aggregate'.");
}
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<InventoryQueryHandler>();
builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
