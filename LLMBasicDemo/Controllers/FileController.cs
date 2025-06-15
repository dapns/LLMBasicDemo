using DocumentFormat.OpenXml.Packaging;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;

namespace LLMBasic.Controllers
{
    public class FileController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private static readonly HttpClient _httpClient = new();

        public FileController(IWebHostEnvironment env)
        {
            _env = env;
        }

        [HttpPost("extract-skills")]
        public async Task<IActionResult> ExtractSkills(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");
            string ext = Path.GetExtension(file.FileName);
            using (var stream = file.OpenReadStream())
            {
                string text = ext switch
                {
                    string p when p.EndsWith(".pdf") => ReadPdf(stream),
                    string p when p.EndsWith(".docx") => ReadDocx(stream),
                    _ => throw new NotSupportedException("Unsupported file type.")
                };

                var skillsPath = Path.Combine(_env.ContentRootPath, "skills.json");
                var knownSkills = JsonSerializer.Deserialize<List<string>>(await System.IO.File.ReadAllTextAsync(skillsPath))!;
                var foundSkills = Extract(text, knownSkills);

                return Ok(new { ExtractedSkills = foundSkills });
            }
        }

        [HttpPost("extract-skills-llm")]
        public async Task<IActionResult> ExtractSkillsLLM(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");
            string ext = Path.GetExtension(file.FileName);
            string text = string.Empty;
            using (var stream = file.OpenReadStream())
            {
                text = ext switch
                {
                    string p when p.EndsWith(".pdf") => ReadPdf(stream),
                    string p when p.EndsWith(".docx") => ReadDocx(stream),
                    _ => throw new NotSupportedException("Unsupported file type.")
                };
            }

            var apiKey = "";
            var skills = await ExtractSkillsAsync(text, apiKey);

            return Ok(new { ExtractedSkills = skills });
        }

        private static string ReadPdf(Stream stream)
        {
            using var doc = PdfDocument.Open(stream);
            return string.Join(Environment.NewLine, doc.GetPages().Select(p => p.Text));
        }

        private static string ReadDocx(Stream stream)
        {
            using var doc = WordprocessingDocument.Open(stream, false);
            return doc.MainDocumentPart?.Document.Body?.InnerText ?? "";
        }

        private static List<string> Extract(string text, List<string> knownSkills)
        {
            return knownSkills
                .Where(skill => text.IndexOf(skill, StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct()
                .ToList();
        }

        private static async Task<List<string>> ExtractSkillsAsync(string resumeText, string apiKey)
        {
            var prompt = @$"Extract all technical and soft skills from the following resume. Respond ONLY with a JSON array of skill strings.Resume:{resumeText}";

            var requestBody = new
            {
                model = "gpt-3.5-turbo",
                messages = new[]
                {
                    new { role = "user", content = prompt }
                },
                temperature = 0.2
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", jsonContent);
            var responseString = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(responseString);
            var resultText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(resultText!)!;
            }
            catch
            {
                return new List<string> { "Could not parse skill list." };
            }
        }

    }
}
