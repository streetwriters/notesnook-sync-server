/*
This file is part of the Notesnook Sync Server project (https://notesnook.com/)

Copyright (C) 2023 Streetwriters (Private) Limited

This program is free software: you can redistribute it and/or modify
it under the terms of the Affero GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
Affero GNU General Public License for more details.

You should have received a copy of the Affero GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace Notesnook.API.Helpers
{
    /// <summary>
    /// Configuration for S3 failover behavior
    /// </summary>
    public class S3FailoverConfig
    {
        /// <summary>
        /// Maximum number of retry attempts per endpoint
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// Delay between retries in milliseconds
        /// </summary>
        public int RetryDelayMs { get; set; } = 1000;

        /// <summary>
        /// Whether to use exponential backoff for retries
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Whether to allow failover for write operations (PUT, POST, DELETE).
        /// Default is false to prevent data consistency issues.
        /// </summary>
        public bool AllowWriteFailover { get; set; } = false;

        /// <summary>
        /// List of exception types that should trigger failover
        /// </summary>
        public HashSet<Type> FailoverExceptions { get; set; } = new()
        {
            typeof(AmazonS3Exception),
            typeof(System.Net.Http.HttpRequestException),
            typeof(System.Net.Sockets.SocketException),
            typeof(System.Threading.Tasks.TaskCanceledException),
            typeof(TimeoutException)
        };

        /// <summary>
        /// List of S3 error codes that should trigger failover
        /// </summary>
        public HashSet<string> FailoverErrorCodes { get; set; } = new()
        {
            "ServiceUnavailable",
            "SlowDown",
            "InternalError",
            "RequestTimeout"
        };
    }

    /// <summary>
    /// Result of a failover operation
    /// </summary>
    public class S3FailoverResult<T>
    {
        public T? Result { get; set; }
        public bool UsedFailover { get; set; }
        public int ClientIndex { get; set; } = 0;
        public int AttemptsUsed { get; set; }
        public Exception? LastException { get; set; }
    }

    /// <summary>
    /// Helper class for S3 operations with automatic failover to multiple endpoints
    /// </summary>
    public class S3FailoverHelper
    {
        private readonly List<AmazonS3Client> clients;
        private readonly S3FailoverConfig config;
        private readonly ILogger? logger;

        /// <summary>
        /// Initialize with a list of S3 clients (first is primary, rest are failover endpoints)
        /// </summary>
        public S3FailoverHelper(
            IEnumerable<AmazonS3Client> clients,
            S3FailoverConfig? config = null,
            ILogger? logger = null)
        {
            if (clients == null) throw new ArgumentNullException(nameof(clients));
            this.clients = new List<AmazonS3Client>(clients);
            if (this.clients.Count == 0) throw new ArgumentException("At least one S3 client is required", nameof(clients));
            this.config = config ?? new S3FailoverConfig();
            this.logger = logger;
        }

        /// <summary>
        /// Initialize with params array of S3 clients
        /// </summary>
        public S3FailoverHelper(
            S3FailoverConfig? config = null,
            ILogger? logger = null,
            params AmazonS3Client[] clients)
        {
            if (clients == null || clients.Length == 0)
                throw new ArgumentException("At least one S3 client is required", nameof(clients));
            this.clients = new List<AmazonS3Client>(clients);
            this.config = config ?? new S3FailoverConfig();
            this.logger = logger;
        }

        /// <summary>
        /// Execute an S3 operation with automatic failover
        /// </summary>
        /// <param name="operation">The S3 operation to execute</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="isWriteOperation">Whether this is a write operation (PUT/POST/DELETE). Write operations only use primary endpoint by default.</param>
        public async Task<T> ExecuteWithFailoverAsync<T>(
            Func<AmazonS3Client, Task<T>> operation,
            string operationName = "S3Operation",
            bool isWriteOperation = false)
        {
            var result = await ExecuteWithFailoverInternalAsync(operation, operationName, isWriteOperation);
            if (result.Result == null)
            {
                throw result.LastException ?? new Exception($"Failed to execute {operationName} on all endpoints");
            }
            return result.Result;
        }

        /// <summary>
        /// Execute an S3 operation with automatic failover and return detailed result
        /// </summary>
        /// <param name="operation">The S3 operation to execute</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="isWriteOperation">Whether this is a write operation (PUT/POST/DELETE). Write operations only use primary endpoint by default.</param>
        private async Task<S3FailoverResult<T>> ExecuteWithFailoverInternalAsync<T>(
            Func<AmazonS3Client, Task<T>> operation,
            string operationName = "S3Operation",
            bool isWriteOperation = false)
        {
            var result = new S3FailoverResult<T>();
            Exception? lastException = null;

            // Determine max clients to try based on write operation flag
            var maxClientsToTry = (isWriteOperation && !config.AllowWriteFailover) ? 1 : clients.Count;

            if (isWriteOperation && !config.AllowWriteFailover && clients.Count > 1)
            {
                logger?.LogDebug(
                    "Write operation {Operation} will only use primary endpoint. Failover is disabled for write operations.",
                    operationName);
            }

            // Try each client in sequence (first is primary, rest are failovers)
            for (int i = 0; i < maxClientsToTry; i++)
            {
                var client = clients[i];
                var clientName = i == 0 ? "primary" : $"failover-{i}";
                var isPrimary = i == 0;

                if (!isPrimary && lastException != null)
                {
                    logger?.LogWarning(lastException,
                        "Previous S3 endpoint failed for {Operation}. Attempting {ClientName} (endpoint {Index}/{Total}).",
                        operationName, clientName, i + 1, maxClientsToTry);
                }

                var (success, value, exception, attempts) = await TryExecuteAsync(client, operation, operationName, clientName);
                result.AttemptsUsed += attempts;

                if (success && value != null)
                {
                    result.Result = value;
                    result.UsedFailover = !isPrimary;
                    result.ClientIndex = i;

                    if (!isPrimary)
                    {
                        logger?.LogInformation(
                            "Successfully failed over to {ClientName} S3 endpoint for {Operation}",
                            clientName, operationName);
                    }

                    return result;
                }

                lastException = exception;

                // If this is not the last client and should retry, log and continue
                if (i < maxClientsToTry - 1 && ShouldFailover(exception))
                {
                    logger?.LogWarning(exception,
                        "Endpoint {ClientName} failed for {Operation}. {Remaining} endpoint(s) remaining.",
                        clientName, operationName, maxClientsToTry - i - 1);
                }
            }

            // All clients failed
            result.LastException = lastException;
            logger?.LogError(lastException,
                "All S3 endpoints failed for {Operation}. Total endpoints tried: {EndpointCount}, Total attempts: {Attempts}",
                operationName, maxClientsToTry, result.AttemptsUsed);

            return result;
        }        /// <summary>
                 /// Try to execute an operation with retries
                 /// </summary>
        private async Task<(bool success, T? value, Exception? exception, int attempts)> TryExecuteAsync<T>(
            AmazonS3Client client,
            Func<AmazonS3Client, Task<T>> operation,
            string operationName,
            string endpointName)
        {
            Exception? lastException = null;
            int attempts = 0;

            for (int retry = 0; retry <= config.MaxRetries; retry++)
            {
                attempts++;
                try
                {
                    var result = await operation(client);
                    return (true, result, null, attempts);
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    if (retry < config.MaxRetries && ShouldRetry(ex))
                    {
                        var delay = CalculateRetryDelay(retry);
                        logger?.LogWarning(ex,
                            "Attempt {Attempt}/{MaxAttempts} failed for {Operation} on {Endpoint}. Retrying in {Delay}ms",
                            retry + 1, config.MaxRetries + 1, operationName, endpointName, delay);

                        await Task.Delay(delay);
                    }
                    else
                    {
                        logger?.LogError(ex,
                            "Operation {Operation} failed on {Endpoint} after {Attempts} attempts",
                            operationName, endpointName, attempts);
                        break;
                    }
                }
            }

            return (false, default, lastException, attempts);
        }

        /// <summary>
        /// Determine if an exception should trigger a retry
        /// </summary>
        private bool ShouldRetry(Exception exception)
        {
            // Check if exception type is in the retry list
            var exceptionType = exception.GetType();
            if (config.FailoverExceptions.Contains(exceptionType))
            {
                // For S3 exceptions, check error codes
                if (exception is AmazonS3Exception s3Exception)
                {
                    return config.FailoverErrorCodes.Contains(s3Exception.ErrorCode);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determine if an exception should trigger failover to secondary endpoint
        /// </summary>
        private bool ShouldFailover(Exception? exception)
        {
            if (exception == null) return false;
            return ShouldRetry(exception);
        }

        /// <summary>
        /// Calculate delay for retry based on retry attempt number
        /// </summary>
        private int CalculateRetryDelay(int retryAttempt)
        {
            if (!config.UseExponentialBackoff)
            {
                return config.RetryDelayMs;
            }

            // Exponential backoff: delay * 2^retryAttempt
            return config.RetryDelayMs * (int)Math.Pow(2, retryAttempt);
        }

        /// <summary>
        /// Execute a void operation with automatic failover
        /// </summary>
        /// <param name="operation">The S3 operation to execute</param>
        /// <param name="operationName">Name of the operation for logging</param>
        /// <param name="isWriteOperation">Whether this is a write operation (PUT/POST/DELETE). Write operations only use primary endpoint by default.</param>
        public async Task ExecuteWithFailoverAsync(
            Func<AmazonS3Client, Task> operation,
            string operationName = "S3Operation",
            bool isWriteOperation = false)
        {
            await ExecuteWithFailoverAsync<object?>(async (client) =>
            {
                await operation(client);
                return null;
            }, operationName, isWriteOperation);
        }
    }

    public static class S3ClientFactory
    {
        public static List<AmazonS3Client> CreateS3Clients(
            string serviceUrls,
            string regions,
            string accessKeyIds,
            string secretKeys,
            bool forcePathStyle = true)
        {
            if (string.IsNullOrWhiteSpace(serviceUrls))
                return new List<AmazonS3Client>();

            var urls = SplitAndTrim(serviceUrls);
            var regionList = SplitAndTrim(regions);
            var keyIds = SplitAndTrim(accessKeyIds);
            var secrets = SplitAndTrim(secretKeys);

            if (urls.Length != regionList.Length ||
                urls.Length != keyIds.Length ||
                urls.Length != secrets.Length)
            {
                throw new ArgumentException("All S3 configuration parameters must have the same number of values");
            }

            var clients = new List<AmazonS3Client>();

            for (int i = 0; i < urls.Length; i++)
            {
                var url = urls[i];
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                // Get corresponding values from other arrays
                var region = regionList[i];
                var keyId = keyIds[i];
                var secret = secrets[i];

                // Validate that all required values are present
                if (string.IsNullOrWhiteSpace(region) ||
                    string.IsNullOrWhiteSpace(keyId) ||
                    string.IsNullOrWhiteSpace(secret))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Skipping S3 client at index {i}: Missing required values (URL={url}, Region={region}, KeyId={keyId?.Length > 0}, Secret={secret?.Length > 0})");
                    continue;
                }

                try
                {
                    var config = new AmazonS3Config
                    {
                        ServiceURL = url,
                        AuthenticationRegion = region,
                        ForcePathStyle = forcePathStyle,
                        SignatureMethod = Amazon.Runtime.SigningAlgorithm.HmacSHA256,
                        SignatureVersion = "4"
                    };

                    var client = new AmazonS3Client(keyId, secret, config);
                    clients.Add(client);
                }
                catch (Exception ex)
                {
                    // Log configuration error but continue with other clients
                    System.Diagnostics.Debug.WriteLine($"Failed to create S3 client for URL {url}: {ex.Message}");
                }
            }

            return clients;
        }

        private static string[] SplitAndTrim(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return Array.Empty<string>();

            return input.Split(';', StringSplitOptions.None)
                       .Select(s => s.Trim())
                       .ToArray();
        }
    }
}
