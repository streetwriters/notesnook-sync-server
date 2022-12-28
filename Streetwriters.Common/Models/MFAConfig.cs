namespace Streetwriters.Common.Models
{
    public class MFAConfig
    {
        public bool IsEnabled { get; set; }
        public string PrimaryMethod { get; set; }
        public string SecondaryMethod { get; set; }
        public int RemainingValidCodes { get; set; }
    }
}
