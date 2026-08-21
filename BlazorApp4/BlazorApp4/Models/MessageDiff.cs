namespace BlazorApp4.Models
{
    public class MessageDiff
    {
        public MessageDb Left { get; set; }
        public MessageDb Right { get; set; }

        public double Similarity { get; set; }
    }
}
