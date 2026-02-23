using System.ComponentModel.DataAnnotations;

namespace Notesnook.API.Models
{
    public class ResetPasswordForm
    {
        [Required]
        public required string NewPassword
        {
            get; set;
        }

        [Required]
        public required UserKeys UserKeys
        {
            get; set;
        }
    }
}