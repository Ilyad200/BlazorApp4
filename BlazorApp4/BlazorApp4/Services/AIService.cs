
using Microsoft.Extensions.Options;
using OpenRouter.NET;
using OpenRouter.NET.Models;

using BlazorApp4.Models;

namespace BlazorApp4.Services
{
    public class AiService
    {
        private readonly OpenRouterClient _client;

        public AiService(IOptions<OpenRouterSettings> options)
        {
            _client = new OpenRouterClient(options.Value.ApiKey);
        }

        public async Task<string> AskAsync(string modelUrl, List<Message> messages)
        {
            var request = new ChatCompletionRequest
            {
                Model = modelUrl,
                Messages = messages
            };

            try
            {
                var response = await _client.CreateChatCompletionAsync(request);
                try
                {
                    return response.Choices[0]?.Message?.Content?.ToString();
                }
                catch
                {
                    return "Нет ответа";
                }
            }
            catch (Exception ex)
            {
                return $"Ошибка запроса: {ex}";
            }
        }
    }

    public class OpenRouterSettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}
