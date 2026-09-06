namespace BlazorApp4.Models
{
    public class VersionDb
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public int Order { get; set; }
        public string Model { get; set; }
        public bool IsOriginal { get; set; } = false;
        public DateTimeOffset CreatedAt { get; set; }

        public int MessageId { get; set; }
        public MessageDb Message { get; set; }

        public int? SnapshotId { get; set; }
        public ChatSnapshot? Snapshot { get; set; }
        public VersionDb() { }

        public VersionDb(VersionDb other)
        {
            Id = other.Id;
            Content = other.Content;
            Order = other.Order;
            Model = other.Model;
            IsOriginal = other.IsOriginal;
            CreatedAt = other.CreatedAt;
            MessageId = other.MessageId;
            Message = other.Message;
            if (other.SnapshotId != null) SnapshotId = other.SnapshotId;
            else SnapshotId = null;
            if (other.Snapshot != null) Snapshot = other.Snapshot;
            else Snapshot = null;
        }
        public static VersionDb CreateCleanVersion(VersionDb other)
        {
            var newVers = new VersionDb
            {
                Content = other.Content,
                Order = other.Order,
                Model = other.Model,
                IsOriginal = other.IsOriginal,
                CreatedAt = other.CreatedAt
            };
            return newVers;
        }
    }
}
