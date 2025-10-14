namespace Streetwriters.Common.Models
{
    public class EmailTemplate
    {
        public int? Id { get; set; }
        public object? Data { get; set; }
        public required string Subject { get; set; }
        public required string Html { get; set; }
        public required string Text { get; set; }
    }
}
