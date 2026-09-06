namespace BlazorApp4.Models
{
    public class ChatSnapshot
    {
        public int Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public int ChatId { get; set; }
        public ChatDb Chat { get; set; } = null!;

        public List<SnapshotEntry> Entries { get; set; } = new();
    }
}
