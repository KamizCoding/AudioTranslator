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
            _httpClient = new HttpClient();
            _openAiApiKey = openAiApiKey ?? throw new ArgumentNullException(nameof(openAiApiKey), "OpenAI API Key is missing.");
        }

        [HttpPost("translate")]
        public async Task<IActionResult> TranslateAudio([FromForm] IFormFile file, [FromForm] string language = "Tamil")
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
            {
                return StatusCode(500, "Error in transcription.");
            }

            var translatedText = await TranslateToLanguage(transcription, language);
            System.IO.File.Delete(tempPath);

            return Ok(new { original_text = transcription, translated_text = translatedText });
        }

        private async Task<string> TranscribeAudio(string filePath)
        {
            using var requestContent = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open);
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");

            requestContent.Add(fileContent, "file", "audio.mp3");
            requestContent.Add(new StringContent("whisper-1"), "model");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/transcriptions", requestContent);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            Console.WriteLine("DEBUG: Whisper API Response => " + jsonResponse);

            using var jsonDoc = JsonDocument.Parse(jsonResponse);

            if (!jsonDoc.RootElement.TryGetProperty("text", out JsonElement textElement))
            {
                throw new Exception("OpenAI Whisper response does not contain 'text' field. Full response: " + jsonResponse);
            }

            return textElement.GetString();
        }

        private async Task<string> TranslateToLanguage(string englishText, string targetLanguage)
        {
            var requestBody = new
            {
                model = "gpt-4",
                messages = new[]
                {
                    new { role = "system", content = $"You are a translator that translates English to {targetLanguage}." },
                    new { role = "user", content = $"Translate this to {targetLanguage}: {englishText}" }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiApiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            Console.WriteLine("DEBUG: GPT-4 Response => " + jsonResponse);

            try
            {
                using var jsonDoc = JsonDocument.Parse(jsonResponse);
                if (jsonDoc.RootElement.TryGetProperty("choices", out JsonElement choicesElement) &&
                    choicesElement.GetArrayLength() > 0 &&
                    choicesElement[0].TryGetProperty("message", out JsonElement messageElement) &&
                    messageElement.TryGetProperty("content", out JsonElement contentElement))
                {
                    return contentElement.GetString()?.Trim() ?? throw new Exception("Unexpected null response from OpenAI GPT-4.");
                }
                else
                {
                    throw new Exception("OpenAI GPT-4 response does not contain the expected 'content' field.");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing OpenAI GPT-4 response: {jsonResponse}. Exception: {ex.Message}");
            }
        }
    }
}
