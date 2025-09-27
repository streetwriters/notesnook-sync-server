using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Services
{
    public class PaddleService(string vendorId, string vendorAuthCode)
    {
#if (DEBUG || STAGING)
        const string PADDLE_BASE_URI = "https://sandbox-vendors.paddle.com/api";
#else
        const string PADDLE_BASE_URI = "https://vendors.paddle.com/api";
#endif

        HttpClient httpClient = new HttpClient();

        public async Task<ListUsersResponse> ListUsersAsync(
            string subscriptionId,
            int results
        )
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/users";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "subscription_id", subscriptionId },
                        { "results_per_page", results.ToString() },
                    }
                )
            );

            return await response.Content.ReadFromJsonAsync<ListUsersResponse>();
        }

        public async Task<ListPaymentsResponse> ListPaymentsAsync(
            string subscriptionId,
            long planId
        )
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/payments";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "subscription_id", subscriptionId },
                        { "is_paid", "1" },
                        { "plan", planId.ToString() },
                        { "is_one_off_charge", "0" },
                    }
                )
            );

            return await response.Content.ReadFromJsonAsync<ListPaymentsResponse>();
        }

        public async Task<ListTransactionsResponse> ListTransactionsAsync(
            string subscriptionId
        )
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/{subscriptionId}/transactions";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                    }
                )
            );

            return await response.Content.ReadFromJsonAsync<ListTransactionsResponse>();
        }

        public async Task<PaddleTransactionUser> FindUserFromOrderAsync(string orderId)
        {
            var url = $"{PADDLE_BASE_URI}/2.0/order/{orderId}/transactions";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                    }
                )
            );
            var transactions = await response.Content.ReadFromJsonAsync<ListTransactionsResponse>();
            if (transactions.Transactions.Length == 0) return null;
            return transactions.Transactions[0].User;
        }

        public async Task<bool> RefundPaymentAsync(string paymentId, string reason = "")
        {
            var url = $"{PADDLE_BASE_URI}/2.0/payment/refund";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "order_id", paymentId },
                        { "reason", reason },
                    }
                )
            );

            var refundResponse = await response.Content.ReadFromJsonAsync<RefundPaymentResponse>();
            return refundResponse.Success;
        }

        public async Task<bool> CancelSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/users_cancel";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "subscription_id", subscriptionId },
                    }
                )
            );

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PauseSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/users/update";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "subscription_id", subscriptionId },
                        { "pause", "true" },
                    }
                )
            );

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResumeSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/2.0/subscription/users/update";
            var httpClient = new HttpClient();
            var response = await httpClient.PostAsync(
                url,
                new FormUrlEncodedContent(
                    new Dictionary<string, string>
                    {
                        { "vendor_id", vendorId },
                        { "vendor_auth_code", vendorAuthCode },
                        { "subscription_id", subscriptionId },
                        { "pause", "false" },
                    }
                )
            );

            return response.IsSuccessStatusCode;
        }
    }
}
