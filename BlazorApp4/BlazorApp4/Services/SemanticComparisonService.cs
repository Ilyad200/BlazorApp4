using BlazorApp4.Models;
using System.Net.Http.Json;
using System.Numerics;

namespace BlazorApp4.Services
{
    public class SemanticComparisonService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SemanticComparisonService> _logger;

        private readonly string _ollamaUrl = "http://ollama:11434/api/embeddings";

        public SemanticComparisonService(IHttpClientFactory httpClientFactory, ILogger<SemanticComparisonService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<List<double>> CompareAsync(
            ChatDb leftChat,
            ChatDb rightChat,
            int leftStartMessageOrder,
            int rightStartMessageOrder)
        {
            var leftMessages = leftChat.Messages
                .Where(m => m.Order >= leftStartMessageOrder)
                .OrderBy(m => m.Order)
                .ToList();

            var rightMessages = rightChat.Messages
                .Where(m => m.Order >= rightStartMessageOrder)
                .OrderBy(m => m.Order)
                .ToList();

            int count = Math.Min(leftMessages.Count, rightMessages.Count);

            var leftToCompare = leftMessages.Take(count).ToList();
            var rightToCompare = rightMessages.Take(count).ToList();

            var leftEmbeddingsTask = GetEmbeddingsBatchAsync(leftToCompare);
            var rightEmbeddingsTask = GetEmbeddingsBatchAsync(rightToCompare);

            await Task.WhenAll(leftEmbeddingsTask, rightEmbeddingsTask);

            var leftEmbeddings = await leftEmbeddingsTask;
            var rightEmbeddings = await rightEmbeddingsTask;

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
            var httpClient = _httpClientFactory.CreateClient();
            var tasks = messages.Select(async message =>
            {
                var text = message.GetCurrentVersion()?.Content ?? "";
                if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

                var payload = new { model = "embeddinggemma:latest", prompt = text };
                try
                {
                    var response = await httpClient.PostAsJsonAsync(_ollamaUrl, payload);
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
        public async Task<double> RecalculateSingleMessageAsync(
            string firstMessage, string secondMessage)
        {
            var firstEmbeddingTask = GetEmbeddingAsync(firstMessage);
            var secondEmbeddingTask = GetEmbeddingAsync(secondMessage);

            await Task.WhenAll(firstEmbeddingTask, secondEmbeddingTask);

            var leftEmbedding = await firstEmbeddingTask;
            var rightEmbedding = await secondEmbeddingTask;

            if (leftEmbedding == null || rightEmbedding == null)
                return 0;

            return CalculateCosineSimilarity(leftEmbedding, rightEmbedding);
        }

        private async Task<float[]> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

            var httpClient = _httpClientFactory.CreateClient();
            var payload = new { model = "embeddinggemma:latest", prompt = text };
            try
            {
                var response = await httpClient.PostAsJsonAsync(_ollamaUrl, payload);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
                return result?.Embedding ?? Array.Empty<float>();
            }
            catch (Exception ex)
            {
                return Array.Empty<float>();
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

            int vectorSize = Vector<float>.Count;
            int i = 0;

            for (; i <= length - vectorSize; i += vectorSize)
            {
                var v1 = new Vector<float>(vec1, i);
                var v2 = new Vector<float>(vec2, i);

                dotProduct += Vector.Dot(v1, v2);
                mag1 += Vector.Dot(v1, v1);
                mag2 += Vector.Dot(v2, v2);
            }

            for (; i < length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                mag1 += vec1[i] * vec1[i];
                mag2 += vec2[i] * vec2[i];
            }

            if (mag1 == 0f || mag2 == 0f) return 0.0;

            return dotProduct / (MathF.Sqrt(mag1) * MathF.Sqrt(mag2));
        }

        public class OllamaEmbeddingResponse
        {
            public float[] Embedding { get; set; } = Array.Empty<float>();
        }
    }
}
