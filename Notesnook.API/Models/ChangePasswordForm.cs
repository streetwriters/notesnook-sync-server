using System.ComponentModel.DataAnnotations;

namespace Notesnook.API.Models
{
    public class ChangePasswordForm
    {
        public string? OldPassword
        {
            get; set;
        }

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

        /// <summary>
        /// Revision of the user the keys were rewrapped from.
        /// </summary>
        public long? Revision
        {
            get; set;
        }
    }
}