var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Exposed so integration tests can use WebApplicationFactory<Program>.
public partial class Program { }
