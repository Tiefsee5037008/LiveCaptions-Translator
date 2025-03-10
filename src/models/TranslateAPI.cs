﻿using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveCaptionsTranslator.models
{
    public static class TranslateAPI
    {
        public static readonly Dictionary<string, Func<string, Task<string>>> TRANSLATE_FUNCS = new()
        {
            { "Ollama", Ollama },
            { "OpenAI", OpenAI },
            { "GoogleTranslate", GoogleTranslate },
            { "OpenRouter", OpenRouter }
        };
        public static Func<string, Task<string>> TranslateFunc
        {
            get => TRANSLATE_FUNCS[App.Settings.ApiName];
        }

        public const int OLLAMA_PORT = 11434;
        public static string Prompt
        {
            get => App.Settings.Prompt;
        }

        private static readonly HttpClient client = new HttpClient() { Timeout = TimeSpan.FromSeconds(5) };

        public static async Task<string> OpenAI(string text)
        {
            var config = App.Settings.CurrentAPIConfig as OpenAIConfig;
            string language = config.SupportedLanguages.TryGetValue(App.Settings.TargetLanguage, out var langValue) 
                ? langValue 
                : App.Settings.TargetLanguage; 
            var requestData = new
            {
                model = config?.ModelName,
                messages = new OpenAIConfig.Message[]
                {
                    new OpenAIConfig.Message { role = "system", content = string.Format(Prompt, language)},
                    new OpenAIConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
                },
                temperature = config?.Temperature,
                max_tokens = 64,
                stream = false
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config?.ApiKey}");

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(config?.ApiUrl, content);
            }
            catch (Exception ex)
            {
                return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<OpenAIConfig.Response>(responseString);
                return responseObj.choices[0].message.content;
            }
            else
                return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] HTTP Error - {response.StatusCode}";
        }

        public static async Task<string> Ollama(string text)
        {
            var apiUrl = $"http://localhost:{OLLAMA_PORT}/api/chat";

            var config = App.Settings.CurrentAPIConfig as OllamaConfig;
            string language = config.SupportedLanguages.TryGetValue(App.Settings.TargetLanguage, out var langValue) 
                ? langValue 
                : App.Settings.TargetLanguage; 
            var requestData = new
            {
                model = config?.ModelName,
                messages = new OllamaConfig.Message[]
                {
                    new OllamaConfig.Message { role = "system", content = string.Format(Prompt, language)},
                    new OllamaConfig.Message { role = "user", content = $"🔤 {text} 🔤" }
                },
                temperature = config?.Temperature,
                max_tokens = 64,
                stream = false
            };

            string jsonContent = JsonSerializer.Serialize(requestData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            client.DefaultRequestHeaders.Clear();

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsync(apiUrl, content);
            }
            catch (Exception ex)
            {
                return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] {ex.Message}";
            }

            if (response.IsSuccessStatusCode)
            {
                string responseString = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<OllamaConfig.Response>(responseString);
                return responseObj.message.content;
            }
            else
                return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] HTTP Error - {response.StatusCode}";
        }

        private static async Task<string> GoogleTranslate(string text)
        {
            var config = App.Settings?.CurrentAPIConfig as GoogleTranslateConfig;
            var language = App.Settings?.TargetLanguage;

            string encodedText = Uri.EscapeDataString(text);
            var url = $"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=auto&tl={language}&q={encodedText}";

            try
            {
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string responseString = await response.Content.ReadAsStringAsync();

                    var responseObj = JsonSerializer.Deserialize<List<List<string>>>(responseString);

                    string translatedText = responseObj[0][0];
                    return translatedText;
                }
                else
                {
                    return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] HTTP Error - {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return App.Settings.HideErrorMessages ? "" : $"[Translation Failed] {ex.Message}";
            }
        }

        public static async Task<string> OpenRouter(string text)
        {
            var config = App.Settings.CurrentAPIConfig as OpenRouterConfig;
            var language = config?.SupportedLanguages[App.Settings.TargetLanguage];

            var requestData = new
            {
                model = config?.ModelName,
                messages = new[]
                {
                    new { role = "system", content = string.Format(Prompt, language)},
                    new { role = "user", content = $"🔤 {text} 🔤" }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json"
                )
            };

            request.Headers.Add("Authorization", $"Bearer {config?.ApiKey}");
            request.Headers.Add("HTTP-Referer", "https://github.com/SakiRinn/LiveCaptionsTranslator");
            request.Headers.Add("X-Title", "LiveCaptionsTranslator");

            var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenRouter API error: {responseContent}");
            }

            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return jsonResponse.GetProperty("choices")[0]
                               .GetProperty("message")
                               .GetProperty("content")
                               .GetString() ?? string.Empty;
        }
    }

    public class ConfigDictConverter : JsonConverter<Dictionary<string, TranslateAPIConfig>>
    {
        public override Dictionary<string, TranslateAPIConfig> Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var configs = new Dictionary<string, TranslateAPIConfig>();
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException("Expected a StartObject token.");

            reader.Read();
            while (reader.TokenType == JsonTokenType.PropertyName)
            {
                string key = reader.GetString();
                reader.Read();

                TranslateAPIConfig config;
                var configType = Type.GetType($"LiveCaptionsTranslator.models.{key}Config");
                if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                    config = (TranslateAPIConfig)JsonSerializer.Deserialize(ref reader, configType, options);
                else
                    throw new JsonException($"Unknown config type for key: {key}");

                configs[key] = config;
                reader.Read();
            }

            if (reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException("Expected an EndObject token.");
            return configs;
        }

        public override void Write(
            Utf8JsonWriter writer, Dictionary<string, TranslateAPIConfig> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var kvp in value)
            {
                writer.WritePropertyName(kvp.Key);

                var configType = Type.GetType($"LiveCaptionsTranslator.models.{kvp.Key}Config");
                if (configType != null && typeof(TranslateAPIConfig).IsAssignableFrom(configType))
                    JsonSerializer.Serialize(writer, kvp.Value, configType, options);
                else
                    throw new JsonException($"Unknown config type for key: {kvp.Key}");
            }
            writer.WriteEndObject();
        }
    }
}
