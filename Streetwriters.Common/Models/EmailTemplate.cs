namespace Streetwriters.Common.Models
{
    public class EmailTemplate
    {
        public int? Id { get; set; }
        public object Data { get; set; }
        public string Subject { get; set; }
        public string Html { get; set; }
        public string Text { get; set; }
    }
}
