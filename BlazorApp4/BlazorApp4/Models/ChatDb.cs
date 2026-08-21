using OpenRouter.NET.Models;

namespace BlazorApp4.Models
{
    public class ChatDb
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        public List<MessageDb> Messages { get; set; } = new();

        // Конструктор для EF (пустой)
        public ChatDb() { }

        // Конструктор для создания нового чата с системным сообщением
        public ChatDb(string name)
        {
            Name = name;
            MessageDb message = new() { Role = "system" };
            message.AddVersion("You are a helpful assistant", null, new());
            Messages.Add(message);
        }

        public ChatDb(ChatDb other)
        {
            Id = other.Id;
            Name = other.Name;
            CreatedAt = other.CreatedAt;
            Messages = new List<MessageDb>(other.Messages.Count);
            foreach (var m in other.Messages)
            {
                Messages.Add(new MessageDb(m));
            }
        }


        // Вспомогательный метод для быстрого формирования списка для API (user + assistant, иногда system)
        public List<Message> GetApiMessages()
        {
            List<Message> messages = new List<Message>();

            foreach (var mes in Messages.OrderBy(m => m.Order))
            {
                if (mes.Role == "user")
                {
                    messages.Add(Message.FromUser(mes.GetCurrentVersion().Content));
                }
                else if (mes.Role == "assistant")
                {
                    messages.Add(Message.FromAssistant(mes.GetCurrentVersion().Content));
                }
                else if (mes.Role == "system")
                {
                    messages.Add(Message.FromSystem(mes.GetCurrentVersion().Content));
                }
            }
            return messages;
        }
        public List<Message> GetMessagesBefor(int messageId)
        {
            List<Message> messages = new();

            MessageDb? lastMessage = Messages.FirstOrDefault(m => m.Id == messageId);
            if (lastMessage == null) return messages;

            foreach (var message in Messages.OrderBy(m => m.Order))
            {
                if (message.Role == "user")
                {
                    messages.Add(Message.FromUser(message.GetCurrentVersion().Content));
                }
                else if (message.Role == "assistant")
                {
                    messages.Add(Message.FromAssistant(message.GetCurrentVersion().Content));
                }
                else if (message.Role == "system")
                {
                    messages.Add(Message.FromSystem(message.GetCurrentVersion().Content));
                }
                if (message.Id == messageId) break;
            }
            return messages;
        }
    }
}
