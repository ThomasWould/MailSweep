using MailSweep.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("healthy", "MailSweep")))
    .WithName("GetHealth");

app.Run();

public partial class Program { }
