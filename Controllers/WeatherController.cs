using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Net.Http;
using System.Text.Json;
using Xunit;
using Moq;

namespace WeatherApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IDistributedCache _cache;
        private readonly IConfiguration _configuration;

        public WeatherController(IHttpClientFactory httpClientFactory, IDistributedCache cache, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _configuration = configuration;
        }

        [HttpGet("getWeather")]
        public async Task<IActionResult> GetWeather(string city)
        {
            // ✅ طباعة apiKey و city
            var apiKey = _configuration["WeatherApiKey"];
            Console.WriteLine($"City: {city}, API Key: {apiKey}");

            // ✅ تاريخ اليوم
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // 1. البحث في Redis إذا كان الكاش موجود
            var cachedWeather = await _cache.GetStringAsync(city + today);
            if (cachedWeather != null)
            {
                return Ok(JsonSerializer.Deserialize<object>(cachedWeather));
            }

            // 2. لو مش موجود في الكاش، نجيب البيانات من API
            var client = _httpClientFactory.CreateClient();

            if (string.IsNullOrEmpty(apiKey))
            {
                return StatusCode(500, "API key is missing.");
            }

            // ✅ URL لليوم الحالي
            var url = $"https://weather.visualcrossing.com/VisualCrossingWebServices/rest/services/timeline/{city}/{today}/{today}?unitGroup=metric&key={apiKey}&contentType=json";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return BadRequest("Error fetching weather data.");
            }

            var weatherData = await response.Content.ReadAsStringAsync();

            // ✅ استخراج البيانات المهمة فقط
            var weatherJson = JsonSerializer.Deserialize<JsonElement>(weatherData);

            var result = new
            {
                City = city,
                Temperature = weatherJson.GetProperty("days")[0].GetProperty("temp").GetDouble(),
                Condition = weatherJson.GetProperty("days")[0].GetProperty("conditions").GetString(),
                Humidity = weatherJson.GetProperty("days")[0].GetProperty("humidity").GetDouble()
            };

            // 3. تخزين البيانات في Redis لمدة 12 ساعة
            await _cache.SetStringAsync(city + today, JsonSerializer.Serialize(result), new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
            });

            return Ok(result);
        }


      
          

        [HttpDelete("clearCache")]
        public async Task<IActionResult> ClearCache(string city)
        {
            await _cache.RemoveAsync(city);
            return Ok($"Cache for '{city}' has been cleared.");
        }
    }
}
