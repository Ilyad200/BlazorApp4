
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
            // Создаём клиента, передавая API-ключ из настроек
            _client = new OpenRouterClient(options.Value.ApiKey);
        }

        public async Task<string> AskAsync(string modelUrl, List<Message> messages)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            foreach (var msg in messages)
            {
                Console.WriteLine(msg.Content);
            }
            Console.ResetColor();
            var request = new ChatCompletionRequest
            {
                //Model = "openrouter/owl-alpha", // Модель можно заменить на любую другую
                Model = modelUrl,
                Messages = messages
                // Параметр Stream по умолчанию = false, поэтому ждём полный ответ
            };

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("CreateChatCompletionAsync");
            // Используем метод CreateChatCompletionAsync
            try
            {
                var response = await _client.CreateChatCompletionAsync(request);
                Console.WriteLine("--- Отправляемые сообщения ---");
                foreach (var m in messages)
                {
                    Console.WriteLine($"[{m.Role}]: {m.Content}");
                }
                // Извлекаем текст ответа из первого варианта
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(response.Choices != null ? "Choises is not null" : "Choises is null");
                Console.ResetColor();
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
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[AiService] {modelUrl}");
                Console.WriteLine($"[AiService] Ошибка запроса: {ex}"); // ex.ToString(), не ex.Message — нужен стектрейс
                Console.ResetColor();
                return "Ошибка запроса";
                //throw; // пробрасываем дальше, чтобы ChatView всё равно показал errorMessage
            }




        }
    }


    // Класс для автоматического связывания с секцией "OpenRouter" из конфигурации
    public class OpenRouterSettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }
}
