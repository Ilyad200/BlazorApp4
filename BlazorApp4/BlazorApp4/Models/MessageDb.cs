using BlazorApp4.Services;

namespace BlazorApp4.Models
{
    public class MessageDb
    {
        public int Id { get; set; }
        public string Role { get; set; }
        public int Order { get; set; }
        public int CurrentVersionOrder { get; set; }
        public List<VersionDb> Versions { get; set; } = new();

        public int ChatId { get; set; }
        public ChatDb Chat { get; set; }
        public MessageDb() { }
        public MessageDb(MessageDb other) 
        {
            Id = other.Id;
            Role = other.Role;
            Order = other.Order;
            CurrentVersionOrder = other.CurrentVersionOrder;
            Versions = new List<VersionDb>(other.Versions.Count);
            foreach (var v in other.Versions)
            {
                Versions.Add(new(v));
            }
            ChatId = other.ChatId;
            Chat = other.Chat;
        }
        public VersionDb AddVersion(string content, string? model, bool isOriginal = false)
        {
            Console.WriteLine("/////////////////////////////////////");
            Console.WriteLine($"Message {Id}: Versions count {Versions.Count}");

            if (model == null) model = "nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free";
            var version = new VersionDb
            {
                Content = content,
                Order = Versions.Count,
                Model = model,
                IsOriginal = isOriginal,
                CreatedAt = DateTimeOffset.UtcNow
            };
            Versions.Add(version);

            CurrentVersionOrder = version.Order;

            return version;
        }

        // Получить текущую версию (обёртка)
        public VersionDb GetCurrentVersion()
        {
            return Versions.FirstOrDefault(v => v.Order == CurrentVersionOrder)!;
        }

        public static MessageDb CreateCleanMessage(MessageDb other)
        {
            var newMes = new MessageDb
            {
                Role = other.Role,
                Order = other.Order,
                CurrentVersionOrder = other.CurrentVersionOrder
            };
            foreach (var v in other.Versions)
            {
                var newVers = VersionDb.CreateCleanVersion(v);
                newVers.Message = newMes;
                newMes.Versions.Add(newVers);
            }
            return newMes;
        }
    }
}
