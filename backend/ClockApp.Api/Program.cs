using ClockApp.Api.Data;
using ClockApp.Api.Time;
using ClockApp.Attendance;
using ClockApp.Attendance.Entities;
using ClockApp.Attendance.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AttendanceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// External Europe/Zurich time API: the only time source for Clock In/Out records.
builder.Services.AddHttpClient<IAttendanceTimeSource, TimeApiIoTimeSource>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["TimeApi:BaseUrl"] ?? "https://timeapi.io/");
    client.Timeout = TimeSpan.FromSeconds(3);
});

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
