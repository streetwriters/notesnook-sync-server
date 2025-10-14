using System.ComponentModel.DataAnnotations;

namespace Notesnook.API.Models
{
    public class DeleteAccountForm
    {
        [Required]
        public required string Password
        {
            get; set;
        }
    }
}