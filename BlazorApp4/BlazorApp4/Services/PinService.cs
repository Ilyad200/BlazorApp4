using BlazorApp4.Models;

namespace BlazorApp4.Services
{
    public class PinService
    {
        public List<PinnedMessage> PinnedMessageList { get; private set; } = new List<PinnedMessage>();

        // Событие, на которое подпишется страница, чтобы знать, когда перерисоваться
        public event Action? OnChange;

        // Метод для добавления нового класса из компонента
        public void AddMessage(int chatId, MessageDb messageDb, VersionDb version, string role, string? model)
        {
            var message = new PinnedMessage(chatId, messageDb, version, role, model, PinnedMessageList.Count);
            PinnedMessageList.Add(message);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("PinService AddMessage");
            Console.ResetColor();
            NotifyStateChanged(); // Уведомляем всех об изменении
        }

        // Метод для удаления (если понадобится)
        public void RemoveMessage(int id)
        {
            var target = PinnedMessageList.FirstOrDefault(m => m.Id == id);
            if (target != null)
                PinnedMessageList.Remove(target);
            NotifyStateChanged();
        }

        // Вспомогательный метод для вызова события
        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
