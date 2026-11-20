using ClockApp.Api.Data;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

var app = builder.Build();

// Opt-in (docker compose sets it): apply migrations and seed the first admin at startup.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Logger);
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
