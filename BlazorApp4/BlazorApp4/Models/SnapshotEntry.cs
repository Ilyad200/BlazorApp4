namespace BlazorApp4.Models
{
    public class SnapshotEntry
    {
        public int Id { get; set; }
        public int SnapshotId { get; set; }
        public ChatSnapshot Snapshot { get; set; } = null!;

        public int MessageId { get; set; }
        public MessageDb Message { get; set; } = null!;

        public int VersionId { get; set; }
        public VersionDb Version { get; set; } = null!;
    }
}
