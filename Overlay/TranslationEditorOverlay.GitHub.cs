using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.Localization.Core;
using AQWMod.Localization.GitHubPr;

namespace AQWMod.Localization.Overlay
{
    // Aba GitHub — login (device flow) e envio de Pull Request com as
    // traduções alteradas. Nada é enviado sem passar pelo preview e pelo
    // clique em "Enviar PR".
    public partial class TranslationEditorOverlay
    {
        private enum GhState { SignedOut, RequestingCode, WaitingApproval, SignedIn, Preview, Sending, Done }

        private GhState _ghState = GhState.SignedOut;
        private bool _ghTokenLoaded;
        private string? _ghToken;
        private string? _ghLogin;
        private string _ghMessage = "";
        private DeviceCodeInfo? _ghCode;
        private ChangeSet? _ghChanges;
        private PullRequestContent? _ghPr;
        private string? _ghPrUrl;
        private CancellationTokenSource? _ghCts;
        private Image? _imgGitHub;

        private static readonly string GhUserAgent = "AQWTranslation/" + PluginInfo.Version;

        private TokenStore GhTokenStore() =>
            new TokenStore(Path.Combine(BepInEx.Paths.ConfigPath, "AQWTranslation.github.token"), new DpapiSecretProtector());

        private static RepoRef GhUpstream()
        {
            var parts = ModConfig.UpdatesRepository.Value.Split('/');
            return new RepoRef { Owner = parts[0], Name = parts.Length > 1 ? parts[1] : "" };
        }

        private void ToggleGitHub()
        {
            if (_viewMode == ViewMode.GitHub) { DoTree(); return; }
            DoGitHub();
        }

        private void DoGitHub()
        {
            foreach (var row in _rows)
                if (!string.IsNullOrWhiteSpace(row.TransFld.text))
                    _buffers[row.Original] = row.TransFld.text;

            _viewMode = ViewMode.GitHub;
            if (_imgSettings != null) _imgSettings.color = BgChip;
            if (_imgGitHub != null)   _imgGitHub.color   = BgChipOn;

            // token salvo de uma sessão anterior
            if (!_ghTokenLoaded)
            {
                _ghTokenLoaded = true;
                _ghToken = GhTokenStore().Load();
                if (_ghToken != null) { _ghState = GhState.SignedIn; _ = GhResolveLoginAsync(); }
            }

            RenderGitHub();
        }

        private void RenderGitHub()
        {
            if (_viewMode != ViewMode.GitHub) return;

            foreach (Transform t in _listContent) Destroy(t.gameObject);
            _rows.Clear();

            GhSection("GitHub");

            switch (_ghState)
            {
                case GhState.SignedOut:
                    GhLine("Entre com a sua conta do GitHub para enviar as suas traduções como Pull Request.");
                    GhLine("O login é feito no site do GitHub: o mod nunca vê a sua senha.", dim: true);
                    GhLine("Permissão pedida: 'public_repo' (acesso a repositórios públicos da sua conta).", dim: true);
                    GhLine("É a menor que o GitHub oferece para criar fork/PR. Você pode revogar em github.com/settings/applications.", dim: true);
                    GhLine("O token fica cifrado (Windows DPAPI) em BepInEx/config, nunca na pasta de traduções.", dim: true);
                    if (_ghMessage.Length > 0) GhLine(_ghMessage);
                    GhButtons(("Entrar com GitHub", 150, BgBtn, StartGitHubLogin));
                    break;

                case GhState.RequestingCode:
                    GhLine("Pedindo o código de login ao GitHub...");
                    GhButtons(("Cancelar", 90, BgIgnBtn, GhCancelLogin));
                    break;

                case GhState.WaitingApproval:
                    GhLine("1. Abra  " + _ghCode!.VerificationUri + "  no navegador (foi aberto automaticamente).");
                    GhLine("2. Digite o código:   " + _ghCode.UserCode);
                    GhLine("3. Autorize o aplicativo. Esta tela atualiza sozinha.", dim: true);
                    GhButtons(
                        ("Abrir no navegador", 140, BgBtn, () => GhOpen(_ghCode.VerificationUri)),
                        ("Copiar código", 110, BgChip, () => GUIUtility.systemCopyBuffer = _ghCode.UserCode),
                        ("Cancelar", 90, BgIgnBtn, GhCancelLogin));
                    break;

                case GhState.SignedIn:
                    GhLine(_ghLogin != null ? "Conectado como  " + _ghLogin : "Conectado ao GitHub.");
                    if (_ghMessage.Length > 0) GhLine(_ghMessage);
                    GhButtons(
                        ("Revisar alterações", 150, BgBtn, GhPreview),
                        ("Sair da conta", 110, BgIgnBtn, GhSignOut));
                    break;

                case GhState.Preview:
                    RenderPreview();
                    break;

                case GhState.Sending:
                    GhLine("Enviando para o GitHub... isso pode levar alguns segundos.");
                    break;

                case GhState.Done:
                    GhLine(_ghMessage);
                    if (_ghPrUrl != null)
                    {
                        GhLine(_ghPrUrl);
                        GhButtons(
                            ("Abrir a PR", 100, BgBtn, () => GhOpen(_ghPrUrl)),
                            ("Copiar link", 100, BgChip, () => GUIUtility.systemCopyBuffer = _ghPrUrl),
                            ("Voltar", 80, BgIgnBtn, () => { _ghState = GhState.SignedIn; _ghMessage = ""; RenderGitHub(); }));
                    }
                    else
                    {
                        GhButtons(("Voltar", 80, BgIgnBtn, () => { _ghState = GhState.SignedIn; RenderGitHub(); }));
                    }
                    break;
            }

            _statsLabel.text = "GitHub — login e envio de Pull Request";
        }

        private void GhSection(string title)
        {
            var h = HRow(_listContent.gameObject, 20, BgHdr);
            Lbl(h, title, 10, FontStyle.Bold, ColGray, 0, true);
        }

        private void GhLine(string text, bool dim = false)
        {
            var r = HRow(_listContent.gameObject, 18, BgRow);
            Lbl(r, text, 10, dim ? FontStyle.Italic : FontStyle.Normal, dim ? ColGray : ColWhite, 0, true);
        }

        private void GhButtons(params (string label, float w, Color bg, Action act)[] bs)
        {
            var r = HRow(_listContent.gameObject, 28, BgRow);
            foreach (var b in bs) Btn(r, b.label, b.w, 24, b.bg, b.act);
        }

        // Mostra exatamente o que será enviado: título, contagens e a descrição
        // completa gerada. Só o botão "Enviar PR" dispara o envio.
        private void RenderPreview()
        {
            var pr = _ghPr!;
            var cs = _ghChanges!;

            GhLine($"Repositório: {ModConfig.UpdatesRepository.Value}   ·   {pr.FileCount} arquivo(s): " +
                   $"{pr.Added} nova(s), {pr.Modified} alterada(s)" + (pr.Removed > 0 ? $", {pr.Removed} removida(s)" : ""));
            GhLine("Título:  " + pr.Title);
            if (pr.Issues.Count > 0)
                GhLine($"{pr.Issues.Count} ponto(s) de atenção serão listados na PR para o revisor (placeholders, vazias, automáticas...).", dim: true);
            if (cs.IgnoredRemovals > 0)
                GhLine($"{cs.IgnoredRemovals} remoção(ões) local(is) NÃO serão enviadas.", dim: true);

            GhButtons(
                ("Enviar PR", 100, BgBtn, GhSend),
                ("Voltar", 80, BgIgnBtn, () => { _ghState = GhState.SignedIn; RenderGitHub(); }));

            GhSection("Descrição que será publicada");
            const int maxLines = 160;
            var lines = pr.Body.Replace("\r", "").Split('\n');
            foreach (var l in lines.Take(maxLines))
                GhLine(l.Length > 150 ? l.Substring(0, 149) + "…" : l, dim: l.StartsWith("<") || l.StartsWith("|"));
            if (lines.Length > maxLines)
                GhLine($"... (+{lines.Length - maxLines} linhas; o texto completo vai na PR)", dim: true);
        }
        // ----------------------------------------------------------- login

        private async void StartGitHubLogin()
        {
            _ghCts?.Cancel();
            _ghCts = new CancellationTokenSource();
            var ct = _ghCts.Token;

            _ghMessage = "";
            _ghState = GhState.RequestingCode;
            RenderGitHub();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var flow = new DeviceFlowClient(http, GitHubSettings.ClientId, GitHubSettings.Scope);

                _ghCode = await flow.RequestCodeAsync(ct);
                _ghState = GhState.WaitingApproval;
                GhOpen(_ghCode.VerificationUri);
                RenderGitHub();

                var result = await flow.PollAsync(_ghCode, ct);
                if (result.Status != DeviceFlowStatus.Authorized)
                {
                    _ghState = GhState.SignedOut;
                    _ghMessage = result.Message;
                    RenderGitHub();
                    return;
                }

                _ghToken = result.AccessToken;
                var saved = GhTokenStore().Save(_ghToken!);
                _ghState = GhState.SignedIn;
                _ghMessage = saved ? "" : "Não foi possível guardar o token com segurança; você ficará conectado só até fechar o jogo.";
                RenderGitHub();
                await GhResolveLoginAsync();
            }
            catch (OperationCanceledException)
            {
                // cancelado pelo usuário: a tela já foi atualizada por GhCancelLogin
            }
            catch (Exception ex)
            {
                _ghState = GhState.SignedOut;
                _ghMessage = "Não foi possível entrar: " + ex.Message;
                RenderGitHub();
            }
        }

        private void GhCancelLogin()
        {
            _ghCts?.Cancel();
            _ghState = GhState.SignedOut;
            _ghMessage = "";
            RenderGitHub();
        }

        private async System.Threading.Tasks.Task GhResolveLoginAsync()
        {
            if (_ghToken == null) return;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var api = new HttpGitHubApi(http, _ghToken, GhUserAgent);
                _ghLogin = await api.GetLoginAsync(CancellationToken.None);
            }
            catch (GitHubApiException ex) when (ex.StatusCode == 401)
            {
                // token revogado/expirado: descarta e volta pro login
                GhTokenStore().Clear();
                _ghToken = null; _ghLogin = null;
                _ghState = GhState.SignedOut;
                _ghMessage = "A sessão do GitHub expirou. Entre novamente.";
            }
            catch
            {
                // sem rede agora: continua "conectado", o login aparece quando der
            }
            RenderGitHub();
        }

        private void GhSignOut()
        {
            _ghCts?.Cancel();
            GhTokenStore().Clear();
            _ghToken = null; _ghLogin = null;
            _ghState = GhState.SignedOut;
            _ghMessage = "Você saiu. Para revogar o acesso de vez, use github.com/settings/applications.";
            RenderGitHub();
        }

        // ----------------------------------------------------- preview / envio

        private void GhPreview()
        {
            try
            {
                var cs = LocalChanges.Compute(TranslationManager.Instance.TranslationsRootPath);
                if (cs.NeverSynced)
                {
                    _ghMessage = "Ainda não houve sincronização com o GitHub (acontece ao abrir o jogo com um release publicado).";
                    RenderGitHub();
                    return;
                }
                if (cs.IsEmpty)
                {
                    _ghMessage = "Nenhuma tradução nova ou alterada em relação ao GitHub.";
                    RenderGitHub();
                    return;
                }

                _ghChanges = cs;
                _ghPr = PullRequestComposer.Compose(cs, new ComposeOptions
                {
                    Locale = ModConfig.Locale.Value,
                    ModVersion = PluginInfo.Version,
                    Edition = PluginInfo.Edition,
                });
                _ghMessage = "";
                _ghState = GhState.Preview;
            }
            catch (Exception ex)
            {
                _ghMessage = "Não foi possível calcular as alterações: " + ex.Message;
            }
            RenderGitHub();
        }

        private async void GhSend()
        {
            if (_ghToken == null || _ghChanges == null || _ghPr == null) return;

            _ghState = GhState.Sending;
            RenderGitHub();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var api = new HttpGitHubApi(http, _ghToken, GhUserAgent);
                var result = await PullRequestSubmitter.SubmitAsync(
                    api, GhUpstream(), _ghChanges, _ghPr, DateTime.UtcNow, CancellationToken.None);

                _ghPrUrl = result.Success && result.PullRequestUrl != null && DeviceFlowClient.IsGitHubHttps(result.PullRequestUrl)
                    ? result.PullRequestUrl : null;
                _ghMessage = result.Success
                    ? "Pull Request criada! O revisor receberá o resumo com os pontos de atenção."
                    : result.Message;
                if (result.Warnings.Count > 0)
                    _ghMessage += "  (" + result.Warnings.Count + " aviso(s): valores já presentes no GitHub foram substituídos.)";
                _ghState = GhState.Done;
            }
            catch (Exception ex)
            {
                _ghMessage = "Falha ao enviar: " + ex.Message;
                _ghPrUrl = null;
                _ghState = GhState.Done;
            }
            RenderGitHub();
        }

        private static void GhOpen(string url)
        {
            // só abre endereços https do próprio GitHub
            if (DeviceFlowClient.IsGitHubHttps(url)) Application.OpenURL(url);
        }
    }
}
