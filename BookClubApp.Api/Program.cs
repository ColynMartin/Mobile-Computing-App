using BookClubApp.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ServerDatabase>();

var app = builder.Build();

// Create the server database and tables before accepting requests.
await app.Services
    .GetRequiredService<ServerDatabase>()
    .InitializeAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "BookClubApp.Api is running.");

app.Run();