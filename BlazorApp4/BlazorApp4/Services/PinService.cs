using BlazorApp4.Models;

namespace BlazorApp4.Services
{
    public class PinService
    {
        public List<PinnedMessage> PinnedMessageList { get; private set; } = new List<PinnedMessage>();

        public event Action? OnChange;

        public void AddMessage(int chatId, MessageDb messageDb, VersionDb version, string role, string? model)
        {
            var message = new PinnedMessage(chatId, messageDb, version, role, model, PinnedMessageList.Count);
            PinnedMessageList.Add(message);
            NotifyStateChanged();
        }

        public void RemoveMessage(int id)
        {
            var target = PinnedMessageList.FirstOrDefault(m => m.Id == id);
            if (target != null)
                PinnedMessageList.Remove(target);
            NotifyStateChanged();
        }

        private void NotifyStateChanged() => OnChange?.Invoke();
    }
}
