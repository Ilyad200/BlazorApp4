using BlazorApp4.Models;
using System.Net.Http.Json;
using System.Numerics;

namespace BlazorApp4.Services
{
    public class SemanticComparisonService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<SemanticComparisonService> _logger;

        // Если Blazor запущен локально (dotnet run): "http://localhost:11434/api/embeddings"
        // Если Blazor запущен в Docker: "http://ollama:11434/api/embeddings"
        private readonly string _ollamaUrl = "http://ollama:11434/api/embeddings";

        public SemanticComparisonService(HttpClient httpClient, ILogger<SemanticComparisonService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Сравнивает два чата с учетом настроек из ChatComparison
        /// </summary>
        public async Task<List<double>> CompareAsync(
            ChatDb leftChat,
            ChatDb rightChat,
            int leftStartMessageOrder,
            int rightStartMessageOrder)
        {
            // 1. Фильтруем и сортируем сообщения по Order, начиная с указанных ID
            var leftMessages = leftChat.Messages
                .Where(m => m.Order >= leftStartMessageOrder)
                .OrderBy(m => m.Order)
                .ToList();

            var rightMessages = rightChat.Messages
                .Where(m => m.Order >= rightStartMessageOrder)
                .OrderBy(m => m.Order)
                .ToList();

            // 2. Ограничиваем количество сравнений, если указано
            int count = Math.Min(leftMessages.Count, rightMessages.Count);

            var leftToCompare = leftMessages.Take(count).ToList();
            var rightToCompare = rightMessages.Take(count).ToList();

            // 3. ПАРАЛЛЕЛЬНАЯ векторизация (ключ к высокой скорости)
            var leftEmbeddingsTask = GetEmbeddingsBatchAsync(leftToCompare);
            var rightEmbeddingsTask = GetEmbeddingsBatchAsync(rightToCompare);

            await Task.WhenAll(leftEmbeddingsTask, rightEmbeddingsTask);

            var leftEmbeddings = await leftEmbeddingsTask;
            var rightEmbeddings = await rightEmbeddingsTask;

            // 4. Попарное сравнение
            var results = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                double similarity = CalculateCosineSimilarity(leftEmbeddings[i], rightEmbeddings[i]);

                results.Add(similarity);
            }

            return results;
        }

        private async Task<List<float[]>> GetEmbeddingsBatchAsync(List<MessageDb> messages)
        {
            var tasks = messages.Select(async message =>
            {
                var text = message.GetCurrentVersion()?.Content ?? "";
                if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

                var payload = new { model = "nomic-embed-text", prompt = text };
                try
                {
                    var response = await _httpClient.PostAsJsonAsync(_ollamaUrl, payload);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
                    return result?.Embedding ?? Array.Empty<float>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка векторизации сообщения {MessageId}", message.Id);
                    return Array.Empty<float>();
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }
        /// <summary>
        /// Пересчитывает соответствие только для одного измененного сообщения
        /// </summary>
        public async Task<double?> RecalculateSingleMessageAsync(
            MessageDb firstMessage, MessageDb secondMessage)
        {

            // Получаем векторы только для этих двух сообщений
            var firstEmbeddingTask = GetEmbeddingAsync(firstMessage);
            var secondEmbeddingTask = GetEmbeddingAsync(secondMessage);

            await Task.WhenAll(firstEmbeddingTask, secondEmbeddingTask);

            var leftEmbedding = await firstEmbeddingTask;
            var rightEmbedding = await secondEmbeddingTask;

            if (leftEmbedding == null || rightEmbedding == null)
                return null;

            return CalculateCosineSimilarity([(float)leftEmbedding], [(float)rightEmbedding]);
        }

        /// <summary>
        /// Получает вектор для одного сообщения
        /// </summary>
        private async Task<float?> GetEmbeddingAsync(MessageDb message)
        {
            var text = message.GetCurrentVersion()?.Content ?? "";
            if (string.IsNullOrWhiteSpace(text)) return null;

            var payload = new
            {
                model = "nomic-embed-text",
                input = text
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(_ollamaUrl, payload);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
                return result?.Embedding?.FirstOrDefault();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка векторизации сообщения {MessageId}", message.Id);
                return null;
            }
        }
        private double CalculateCosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1.Length == 0 || vec2.Length == 0 || vec1.Length != vec2.Length)
                return 0.0;

            int length = vec1.Length;
            float dotProduct = 0f;
            float mag1 = 0f;
            float mag2 = 0f;

            // Используем SIMD для ускорения вычислений (Vector<float> обрабатывает несколько значений за такт)
            int vectorSize = Vector<float>.Count;
            int i = 0;

            // Основная часть с SIMD
            for (; i <= length - vectorSize; i += vectorSize)
            {
                var v1 = new Vector<float>(vec1, i);
                var v2 = new Vector<float>(vec2, i);

                dotProduct += Vector.Dot(v1, v2);
                mag1 += Vector.Dot(v1, v1);
                mag2 += Vector.Dot(v2, v2);
            }

            // Хвостовая часть (если длина не кратна vectorSize)
            for (; i < length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                mag1 += vec1[i] * vec1[i];
                mag2 += vec2[i] * vec2[i];
            }

            if (mag1 == 0f || mag2 == 0f) return 0.0;

            return dotProduct / (MathF.Sqrt(mag1) * MathF.Sqrt(mag2));
        }

        // Вспомогательные классы для десериализации и результата
        public class OllamaEmbeddingResponse
        {
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
