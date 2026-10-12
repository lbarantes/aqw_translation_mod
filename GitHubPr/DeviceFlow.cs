using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AQWMod.Localization.GitHubPr
{
    public static class GitHubSettings
    {
        // Client ID do OAuth App "AQW Translation Mod" (device flow habilitado).
        // É público por natureza — o device flow não usa client secret, e
        // nenhum segredo é guardado neste código.
        public const string ClientId = "Ov23lirXNtCqmHiqLcx5";

        // public_repo: necessário pra criar fork/branch/PR em repositório
        // público. É um escopo largo (o GitHub não oferece um mais estreito
        // pra OAuth App) — a tela de login diz isso ao usuário.
        public const string Scope = "public_repo";
    }

    public sealed class DeviceCodeInfo
    {
        public string DeviceCode { get; set; } = "";
        public string UserCode { get; set; } = "";
        public string VerificationUri { get; set; } = "";
        public int ExpiresInSeconds { get; set; }
        public int IntervalSeconds { get; set; } = 5;
    }

    public enum DeviceFlowStatus { Authorized, Denied, Expired, Failed }

    public sealed class DeviceFlowResult
    {
        public DeviceFlowStatus Status { get; set; }
        public string? AccessToken { get; set; }
        public string Message { get; set; } = "";
    }

    // OAuth device flow do GitHub (RFC 8628): o mod pede um código, o usuário
    // digita em github.com/login/device e o mod fica consultando até a
    // aprovação. Não precisa de servidor, de redirect nem de segredo.
    public sealed class DeviceFlowClient
    {
        private const string CodeUrl  = "https://github.com/login/device/code";
        private const string TokenUrl = "https://github.com/login/oauth/access_token";
        private const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

        private readonly HttpClient _http;
        private readonly string _clientId;
        private readonly string _scope;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public DeviceFlowClient(HttpClient http, string clientId, string scope,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _http = http;
            _clientId = clientId;
            _scope = scope;
            _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
        }

        public async Task<DeviceCodeInfo> RequestCodeAsync(CancellationToken ct)
        {
            var json = await PostFormAsync(CodeUrl, new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["scope"]     = _scope,
            }, ct).ConfigureAwait(false);

            var err = (string?)json["error"];
            if (err != null)
                throw new InvalidOperationException("O GitHub recusou o pedido de login: " + ((string?)json["error_description"] ?? err));

            var info = new DeviceCodeInfo
            {
                DeviceCode       = (string?)json["device_code"] ?? "",
                UserCode         = (string?)json["user_code"] ?? "",
                VerificationUri  = (string?)json["verification_uri"] ?? "",
                ExpiresInSeconds = (int?)json["expires_in"] ?? 900,
                IntervalSeconds  = Math.Max(1, (int?)json["interval"] ?? 5),
            };

            if (info.DeviceCode.Length == 0 || info.UserCode.Length == 0)
                throw new InvalidOperationException("Resposta inesperada do GitHub ao pedir o código de login.");

            // só abre/mostra um endereço do próprio GitHub, nunca o que vier cru
            if (!IsGitHubHttps(info.VerificationUri))
                throw new InvalidOperationException("Endereço de verificação inesperado: " + info.VerificationUri);

            return info;
        }

        public async Task<DeviceFlowResult> PollAsync(DeviceCodeInfo info, CancellationToken ct)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, info.IntervalSeconds));
            var deadline = DateTime.UtcNow.AddSeconds(info.ExpiresInSeconds);

            while (DateTime.UtcNow < deadline)
            {
                await _delay(interval, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var json = await PostFormAsync(TokenUrl, new Dictionary<string, string>
                {
                    ["client_id"]   = _clientId,
                    ["device_code"] = info.DeviceCode,
                    ["grant_type"]  = GrantType,
                }, ct).ConfigureAwait(false);

                var token = (string?)json["access_token"];
                if (!string.IsNullOrEmpty(token))
                    return new DeviceFlowResult { Status = DeviceFlowStatus.Authorized, AccessToken = token };

                switch ((string?)json["error"])
                {
                    case "authorization_pending":
                        continue;
                    case "slow_down":
                        // o GitHub manda o novo intervalo; senão soma 5s (RFC 8628)
                        interval = TimeSpan.FromSeconds((int?)json["interval"] ?? (int)interval.TotalSeconds + 5);
                        continue;
                    case "expired_token":
                        return new DeviceFlowResult { Status = DeviceFlowStatus.Expired, Message = "O código expirou. Tente entrar de novo." };
                    case "access_denied":
                        return new DeviceFlowResult { Status = DeviceFlowStatus.Denied, Message = "Login recusado no GitHub." };
                    default:
                        return new DeviceFlowResult
                        {
                            Status  = DeviceFlowStatus.Failed,
                            Message = (string?)json["error_description"] ?? (string?)json["error"] ?? "Resposta inesperada do GitHub.",
                        };
                }
            }

            return new DeviceFlowResult { Status = DeviceFlowStatus.Expired, Message = "O código expirou. Tente entrar de novo." };
        }

        internal static bool IsGitHubHttps(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
            return u.Scheme == Uri.UriSchemeHttps &&
                   (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                    u.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<JObject> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
            req.Headers.Accept.ParseAdd("application/json");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            // o GitHub devolve 200 mesmo com "error" no corpo; só falha de
            // transporte/servidor chega aqui como status de erro
            if (!resp.IsSuccessStatusCode && string.IsNullOrWhiteSpace(text))
                throw new HttpRequestException($"GitHub respondeu HTTP {(int)resp.StatusCode}.");

            try { return JObject.Parse(text); }
            catch { throw new HttpRequestException($"Resposta ilegível do GitHub (HTTP {(int)resp.StatusCode})."); }
        }
    }
}
