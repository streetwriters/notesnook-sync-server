using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Streetwriters.Common.Models;

namespace Streetwriters.Common.Services
{
    public class PaddleBillingService
    {
#if DEBUG
        private const string PADDLE_BASE_URI = "https://sandbox-api.paddle.com";
#else
        private const string PADDLE_BASE_URI = "https://api.paddle.com";
#endif
        private readonly HttpClient httpClient = new();
        public PaddleBillingService(string paddleApiKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", paddleApiKey);
        }

        public async Task<GetSubscriptionResponse?> GetSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}";
            var response = await httpClient.GetAsync(url);
            return await response.Content.ReadFromJsonAsync<GetSubscriptionResponse>();
        }

        public async Task<GetTransactionResponse?> GetTransactionAsync(string transactionId)
        {
            var url = $"{PADDLE_BASE_URI}/transactions/{transactionId}";
            var response = await httpClient.GetAsync(url);
            return await response.Content.ReadFromJsonAsync<GetTransactionResponse>();
        }

        public async Task<GetTransactionInvoiceResponse?> GetTransactionInvoiceAsync(string transactionId)
        {
            var url = $"{PADDLE_BASE_URI}/transactions/{transactionId}/invoice";
            var response = await httpClient.GetAsync(url);
            return await response.Content.ReadFromJsonAsync<GetTransactionInvoiceResponse>();
        }

        public async Task<ListTransactionsResponseV2?> ListTransactionsAsync(string? subscriptionId = null, string? customerId = null, string[]? status = null, string[]? origin = null)
        {
            var url = $"{PADDLE_BASE_URI}/transactions";
            var parameters = new Dictionary<string, string?>()
            {
                { "subscription_id", subscriptionId },
                { "customer_id", customerId },
                { "status", string.Join(',', status ?? ["billed","completed"]) },
                { "order_by", "billed_at[DESC]" }
            };
            if (origin is not null) parameters.Add("origin", string.Join(',', origin));
            var response = await httpClient.GetAsync(QueryHelpers.AddQueryString(url, parameters));

            return await response.Content.ReadFromJsonAsync<ListTransactionsResponseV2>();
        }

        public async Task<bool> RefundTransactionAsync(string transactionId, string transactionItemId, string reason = "")
        {
            var url = $"{PADDLE_BASE_URI}/adjustments";
            var response = await httpClient.PostAsync(url, JsonContent.Create(new Dictionary<string, object>
            {
                { "action", "refund" },
                {
                    "items",
                    new object[]
                    {
                        new Dictionary<string, string> {
                            {"item_id", transactionItemId},
                            {"type", "full"}
                        }
                    }
                },
                { "reason", reason },
                { "transaction_id", transactionId }
            }));
            return response.IsSuccessStatusCode;
        }

        public async Task<SubscriptionPreviewResponse?> PreviewSubscriptionChangeAsync(string subscriptionId, string newProductId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}/preview";
            var response = await httpClient.PatchAsync(url, JsonContent.Create(new
            {
                proration_billing_mode = "prorated_immediately",
                items = new[] { new { price_id = newProductId, quantity = 1 } }
            }));
            return await response.Content.ReadFromJsonAsync<SubscriptionPreviewResponse>();
        }

        public async Task<bool> ChangeSubscriptionAsync(string subscriptionId, string newProductId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}";
            var response = await httpClient.PatchAsync(url, JsonContent.Create(new
            {
                proration_billing_mode = "prorated_immediately",
                items = new[] { new { price_id = newProductId, quantity = 1 } }
            }));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> CancelSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}/cancel";
            var response = await httpClient.PostAsync(url, JsonContent.Create(new { effective_from = "immediately" }));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PauseSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}/pause";
            var response = await httpClient.PostAsync(url, JsonContent.Create(new { }));
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResumeSubscriptionAsync(string subscriptionId)
        {
            var url = $"{PADDLE_BASE_URI}/subscriptions/{subscriptionId}";
            var response = await httpClient.PatchAsync(url, JsonContent.Create(new Dictionary<string, string?>
            {
                {"scheduled_change", null}
            }));
            return response.IsSuccessStatusCode;
        }
    }
}