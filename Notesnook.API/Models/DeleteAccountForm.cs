using System.ComponentModel.DataAnnotations;

namespace Notesnook.API.Models
{
    public class DeleteAccountForm
    {
        [Required]
        public string Password
        {
            get; set;
        }
    }
}