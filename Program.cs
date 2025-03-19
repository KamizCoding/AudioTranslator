using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

string? apiKeyFromEnv = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? apiKeyFromConfig = builder.Configuration["OpenAI:ApiKey"];

string openAiApiKey = (apiKeyFromEnv ?? apiKeyFromConfig ?? string.Empty).Trim();

Console.WriteLine($"DEBUG: OpenAI API Key from Env: {(apiKeyFromEnv?.Length > 10 ? apiKeyFromEnv.Substring(0, 5) + "********" : "Not Set")}");
Console.WriteLine($"DEBUG: OpenAI API Key from Config: {(apiKeyFromConfig?.Length > 10 ? apiKeyFromConfig.Substring(0, 5) + "********" : "Not Set")}");
Console.WriteLine($"DEBUG: Final OpenAI API Key: {(openAiApiKey.Length > 10 ? openAiApiKey.Substring(0, 5) + "********" : "Not Set")}");

if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey == "OPENAI_API_KEY")
{
    throw new Exception("❌ OpenAI API Key is missing or invalid! Set it as an environment variable.");
}

builder.Services.AddSingleton(openAiApiKey);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 104857600;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins("http://localhost:5173")
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

app.UseCors("AllowReactApp");
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("🚀 Audio Translator API is running on:");
Console.WriteLine($"   ➤ HTTP: http://0.0.0.0:{port}");
Console.WriteLine($"   ➤ Swagger UI: http://0.0.0.0:{port}/swagger");

app.Run();
