using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables
builder.Configuration.AddEnvironmentVariables();

// Load OpenAI API key from env or config
string? apiKeyFromEnv = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
string? apiKeyFromConfig = builder.Configuration["OpenAI:ApiKey"];
string openAiApiKey = (apiKeyFromEnv ?? apiKeyFromConfig ?? string.Empty).Trim();

Console.WriteLine($"DEBUG: OpenAI API Key: {(openAiApiKey.Length > 10 ? openAiApiKey.Substring(0, 5) + "********" : "Not Set")}");

if (string.IsNullOrEmpty(openAiApiKey) || openAiApiKey == "OPENAI_API_KEY")
{
    throw new Exception("❌ OpenAI API Key is missing or invalid! Set it as an environment variable.");
}

builder.Services.AddSingleton(openAiApiKey);

// Allow large form upload (up to 500MB)
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000;
});

// Set max body size for Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 524288000;
});

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Correct CORS setup
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy.WithOrigins("https://audiotranslator-frontend.vercel.app")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

var app = builder.Build();

// Enable Swagger in development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ✅ Apply CORS before controllers
app.UseCors("AllowFrontend");

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Bind to port
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

Console.WriteLine("🚀 Audio Translator API is running on:");
Console.WriteLine($"   ➤ HTTP: http://0.0.0.0:{port}");
Console.WriteLine($"   ➤ Swagger UI: http://0.0.0.0:{port}/swagger");

app.Run();
