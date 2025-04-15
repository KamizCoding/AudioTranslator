using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System;

namespace AudioTranslatorAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AudioController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _openAiApiKey;

        public AudioController(string openAiApiKey)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(15)
            };
            _openAiApiKey = openAiApiKey ?? throw new ArgumentNullException(nameof(openAiApiKey), "OpenAI API Key is missing.");
        }

        [HttpPost("translate")]
        public async Task<IActionResult> TranslateAudio(
            [FromForm] IFormFile file,
            [FromForm] string audioLanguage = "English",
            [FromForm] string language = "Tamil")
        {
            if (file == null || file.Length == 0)
                return BadRequest("Invalid file. Please upload an audio file.");

            var tempPath = Path.GetTempFileName();
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var transcription = await TranscribeAudio(tempPath);
            if (string.IsNullOrEmpty(transcription))
                return StatusCode(500, "Error in transcription.");

            var targetLanguage = audioLanguage.Equals("Tamil", StringComparison.OrdinalIgnoreCase)
                ? "English"
                : language;

            var translatedText = await TranslateToLanguage(transcription, targetLanguage);
            System.IO.File.Delete(tempPath);

            return Ok(new { original_text = transcription, translated_text = translatedText });
        }

        [HttpPost("mic-translate")]
        public async Task<IActionResult> MicTranslate([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No audio file received.");

            Console.WriteLine("🔥 Middleware upload received: " + file.FileName);

            var tempPath = Path.GetTempFileName();
            using (var stream = new FileStream(tempPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var transcription = await TranscribeAudio(tempPath);
            if (string.IsNullOrEmpty(transcription))
                return StatusCode(500, "Error in transcription.");

            var translatedText = await TranslateToLanguage(transcription, "English");
            System.IO.File.Delete(tempPath);

            var resultObject = new
            {
                transcribed_text = transcription,
                translated_text = translatedText,
                timestamp = DateTime.UtcNow.ToString("o")
            };

            var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "results", "latest.json");
            var jsonDir = Path.GetDirectoryName(jsonPath);
            if (!Directory.Exists(jsonDir))
            {
                Directory.CreateDirectory(jsonDir);
            }

            await System.IO.File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(resultObject, new JsonSerializerOptions { WriteIndented = true }));

            return Ok(resultObject);
        }



        private async Task<string> TranscribeAudio(string filePath)
        {
            using var requestContent = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");

            requestContent.Add(fileContent, "file", "mic_audio.webm");
            requestContent.Add(new StringContent("whisper-1"), "model");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            try
            {
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", requestContent);
                var jsonResponse = await response.Content.ReadAsStringAsync();
                Console.WriteLine("DEBUG: Whisper API Response => " + jsonResponse);

                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                if (!jsonDoc.RootElement.TryGetProperty("text", out JsonElement textElement))
                    throw new Exception("OpenAI Whisper response does not contain 'text' field. Full response: " + jsonResponse);

                return textElement.GetString();
            }
            catch (TaskCanceledException ex)
            {
                Console.WriteLine("❌ Transcription request timed out or was cancelled.");
                throw;
            }
        }

        private async Task<string> TranslateToLanguage(string originalText, string targetLanguage)
        {
            var requestBody = new
            {
                model = "gpt-4",
                messages = new[]
                {
                    new { role = "system", content = $"You are a translator that translates to {targetLanguage}." },
                    new { role = "user", content = $"Translate this to {targetLanguage}: {originalText}" }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            Console.WriteLine("DEBUG: GPT-4 Response => " + jsonResponse);

            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            if (jsonDoc.RootElement.TryGetProperty("choices", out JsonElement choicesElement) &&
                choicesElement.GetArrayLength() > 0 &&
                choicesElement[0].TryGetProperty("message", out JsonElement messageElement) &&
                messageElement.TryGetProperty("content", out JsonElement contentElement))
            {
                return contentElement.GetString()?.Trim() ?? throw new Exception("Unexpected null response from OpenAI GPT-4.");
            }

            throw new Exception("OpenAI GPT-4 response does not contain the expected 'content' field.");
        }
    }
}
