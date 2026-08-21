namespace BlazorApp4.Models
{
    public class ChatComparison
    {
        public int LeftChatId { get; set; }
        public int RightChatId { get; set; }

        public int LeftStartMessageId { get; set; }
        public int RightStartMessageId { get; set; }

        public int? CountComparisons { get; set; }
    }
}
