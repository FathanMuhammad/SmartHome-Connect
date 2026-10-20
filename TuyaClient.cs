using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SmartHomeAcController
{
    public class TuyaResponseResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string RawJson { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    public class TuyaClient
    {
        private readonly HttpClient _httpClient;
        public AppConfig Config { get; }

        private string? _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public event Action<string, bool>? OnLog;

        public TuyaClient(AppConfig config)
        {
            Config = config;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }

        private void Log(string message, bool isError = false)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}", isError);
        }

        /// <summary>
        /// Gets or refreshes the Tuya Cloud Access Token
        /// </summary>
        public async Task<TuyaResponseResult> EnsureAccessTokenAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return new TuyaResponseResult
                {
                    Success = true,
                    StatusCode = 200,
                    RawJson = $"{{\"cached\": true, \"access_token\": \"{_accessToken}\", \"expires\": \"{_tokenExpiry:O}\"}}"
                };
            }

            if (string.IsNullOrWhiteSpace(Config.ClientSecret))
            {
                string msg = "Client Secret is empty! Please configure your Tuya Client Secret first.";
                Log(msg, true);
                return new TuyaResponseResult { Success = false, ErrorMessage = msg };
            }

            string path = "/v1.0/token?grant_type=1";
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // StringToSign = HTTPMethod + "\n" + SHA256(body).ToLower() + "\n" + "" + "\n" + Path
            string emptyBodyHash = ComputeSha256Hex("");
            string stringToSign = $"GET\n{emptyBodyHash}\n\n{path}";

            // For Token: SignString = ClientId + Timestamp + StringToSign
            string signString = $"{Config.ClientId}{timestamp}{stringToSign}";
            string sign = ComputeHmacSha256Hex(signString, Config.ClientSecret);

            string fullUrl = $"{Config.BaseUrl.TrimEnd('/')}{path}";
            Log($"Requesting Token: GET {path}...");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
                request.Headers.Add("client_id", Config.ClientId);
                request.Headers.Add("sign", sign);
                request.Headers.Add("t", timestamp);
                request.Headers.Add("sign_method", "HMAC-SHA256");

                using var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                Log($"Response Status: {(int)response.StatusCode} {response.StatusCode}");

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                bool isSuccess = false;
                if (root.TryGetProperty("success", out var successProp))
                {
                    isSuccess = successProp.GetBoolean();
                }

                if (isSuccess && root.TryGetProperty("result", out var resultProp))
                {
                    if (resultProp.TryGetProperty("access_token", out var tokenProp))
                    {
                        _accessToken = tokenProp.GetString();
                        int expireSeconds = 7200;
                        if (resultProp.TryGetProperty("expire_time", out var expireProp))
                        {
                            expireSeconds = expireProp.GetInt32();
                        }
                        // Refresh 5 minutes before actual expiry
                        _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(expireSeconds - 300, 60));

                        Log($"Access Token acquired successfully! Valid for {expireSeconds}s.");
                        return new TuyaResponseResult
                        {
                            Success = true,
                            StatusCode = (int)response.StatusCode,
                            RawJson = FormatJson(responseBody)
                        };
                    }
                }

                string errorMsg = "Failed to obtain access token.";
                if (root.TryGetProperty("msg", out var msgProp))
                {
                    errorMsg = $"Tuya Error: {msgProp.GetString()}";
                }
                Log(errorMsg, true);

                return new TuyaResponseResult
                {
                    Success = false,
                    StatusCode = (int)response.StatusCode,
                    RawJson = FormatJson(responseBody),
                    ErrorMessage = errorMsg
                };
            }
            catch (Exception ex)
            {
                Log($"Network / Token Error: {ex.Message}", true);
                return new TuyaResponseResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    RawJson = $"{{\"error\": \"{ex.Message}\"}}"
                };
            }
        }

        /// <summary>
        /// Sends AC Command via Tuya Cloud Smart IR Blaster endpoint
        /// </summary>
        public async Task<TuyaResponseResult> SendAcCommandAsync(int power, int temp, int mode, int wind)
        {
            // Ensure token is ready
            var tokenResult = await EnsureAccessTokenAsync();
            if (!tokenResult.Success || string.IsNullOrEmpty(_accessToken))
            {
                return tokenResult;
            }

            // Resolve Parent / Child IDs based on Swap setting
            string infraredId = Config.IsSwapped ? Config.RemoteId : Config.InfraredId;
            string remoteId = Config.IsSwapped ? Config.InfraredId : Config.RemoteId;

            string path = $"/v2.0/infrareds/{infraredId}/air-conditioners/{remoteId}/scenes/command";
            string timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

            // Payload: {"power": <0/1>, "temp": <16-30>, "mode": <0-4>, "wind": <0-3>}
            var payloadObj = new
            {
                power = power,
                temp = temp,
                mode = mode,
                wind = wind
            };
            string jsonBody = JsonSerializer.Serialize(payloadObj);

            // Compute body hash
            string bodyHash = ComputeSha256Hex(jsonBody);

            // StringToSign = HTTPMethod + "\n" + SHA256(body).ToLower() + "\n" + "" + "\n" + Path
            string stringToSign = $"POST\n{bodyHash}\n\n{path}";

            // For Command: SignString = ClientId + AccessToken + Timestamp + StringToSign
            string signString = $"{Config.ClientId}{_accessToken}{timestamp}{stringToSign}";
            string sign = ComputeHmacSha256Hex(signString, Config.ClientSecret);

            string fullUrl = $"{Config.BaseUrl.TrimEnd('/')}{path}";
            Log($"Sending AC Command: POST {path}");
            Log($"Payload: {jsonBody}");

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
                request.Headers.Add("client_id", Config.ClientId);
                request.Headers.Add("access_token", _accessToken);
                request.Headers.Add("sign", sign);
                request.Headers.Add("t", timestamp);
                request.Headers.Add("sign_method", "HMAC-SHA256");

                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                Log($"Response Status: {(int)response.StatusCode} {response.StatusCode}");

                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                bool isSuccess = false;
                if (root.TryGetProperty("success", out var successProp))
                {
                    isSuccess = successProp.GetBoolean();
                }

                if (isSuccess)
                {
                    Log("AC Command executed successfully by IR Blaster!");
                    return new TuyaResponseResult
                    {
                        Success = true,
                        StatusCode = (int)response.StatusCode,
                        RawJson = FormatJson(responseBody)
                    };
                }
                else
                {
                    string errorMsg = "Command rejected by Tuya API.";
                    if (root.TryGetProperty("msg", out var msgProp))
                    {
                        errorMsg = $"Tuya Msg: {msgProp.GetString()}";
                    }
                    if (root.TryGetProperty("code", out var codeProp))
                    {
                        errorMsg += $" (Code: {codeProp.GetInt32()})";
                    }

                    // If token invalid / expired (e.g., error code 1010 or 1004)
                    if (responseBody.Contains("token invalid", StringComparison.OrdinalIgnoreCase) ||
                        responseBody.Contains("token expired", StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Token expired or invalid. Invalidating token cache...", true);
                        _accessToken = null;
                    }

                    Log(errorMsg, true);
                    return new TuyaResponseResult
                    {
                        Success = false,
                        StatusCode = (int)response.StatusCode,
                        RawJson = FormatJson(responseBody),
                        ErrorMessage = errorMsg
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Command Network Error: {ex.Message}", true);
                return new TuyaResponseResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    RawJson = $"{{\"error\": \"{ex.Message}\"}}"
                };
            }
        }

        private static string ComputeSha256Hex(string input)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = SHA256.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static string ComputeHmacSha256Hex(string message, string secret)
        {
            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(secretBytes);
            byte[] hashBytes = hmac.ComputeHash(messageBytes);
            return Convert.ToHexString(hashBytes).ToUpperInvariant();
        }

        private static string FormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return json;
            }
        }
    }
}
