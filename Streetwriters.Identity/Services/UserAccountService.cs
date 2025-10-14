using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;
using Streetwriters.Identity.Interfaces;
using Streetwriters.Identity.Models;

namespace Streetwriters.Identity.Services
{
    public class UserAccountService(UserManager<User> userManager, IMFAService mfaService) : IUserAccountService
    {
        public async Task<UserModel?> GetUserAsync(string clientId, string userId)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || !await UserService.IsUserValidAsync(userManager, user, clientId))
                return null;

            var claims = await userManager.GetClaimsAsync(user);
            var marketingConsentClaim = claims.FirstOrDefault((claim) => claim.Type == $"{clientId}:marketing_consent");

            if (await userManager.IsEmailConfirmedAsync(user) && !await userManager.GetTwoFactorEnabledAsync(user))
            {
                await mfaService.EnableMFAAsync(user, MFAMethods.Email);
                user = await userManager.FindByIdAsync(userId);
                ArgumentNullException.ThrowIfNull(user);
            }
            ArgumentNullException.ThrowIfNull(user.Email);

            return new UserModel
            {
                UserId = user.Id.ToString(),
                Email = user.Email,
                IsEmailConfirmed = user.EmailConfirmed,
                MarketingConsent = marketingConsentClaim == null,
                MFA = new MFAConfig
                {
                    IsEnabled = user.TwoFactorEnabled,
                    PrimaryMethod = mfaService.GetPrimaryMethod(user),
                    SecondaryMethod = mfaService.GetSecondaryMethod(user),
                    RemainingValidCodes = await mfaService.GetRemainingValidCodesAsync(user)
                }
            };
        }

        public async Task DeleteUserAsync(string clientId, string userId, string password)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null || !await UserService.IsUserValidAsync(userManager, user, clientId)) return;

            if (!await userManager.CheckPasswordAsync(user, password)) throw new Exception("Wrong password.");

            await userManager.DeleteAsync(user);
        }
    }
}