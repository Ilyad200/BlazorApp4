namespace BlazorApp4.Models
{
    public class PinnedMessage
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public int MessageDbId { get; set; }
        public MessageDb MessageDb { get; set; }
        public int VersionId { get; set; }
        public VersionDb Version { get; set; }
        public string Role { get; set; }
        public string? Model { get; set; }
        public int Order { get; set; }

        public PinnedMessage(int chatId, MessageDb messageDb, VersionDb version, string role, string? model, int order)
        {
            ChatId = chatId;
            MessageDbId = messageDb.Id;
            MessageDb = messageDb;
            VersionId = version.Id;
            Version = version;
            Role = role;
            Model = model;
            Order = order;
        }
    }
}
