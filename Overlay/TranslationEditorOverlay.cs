using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AQWMod.Localization.Core;
using AQWMod.Localization.Interceptors;
using AQWMod.Localization.Capture;
using AQWMod.Localization.Repository;
using AQWMod.Localization.Translation;

namespace AQWMod.Localization.Overlay
{
    // Arquivo núcleo da partial class: tipos internos (EntryRow/AutoPreviewRow/
    // ViewMode), campos privados (UI refs, estado, paleta, fonte) e o ciclo
    // de vida do MonoBehaviour (Awake/Update/SetVisible/QuickCheckAndRescan).
    // As demais responsabilidades estão nos arquivos irmãos
    // TranslationEditorOverlay.*.cs — todos a mesma classe (partial), só
    // divididos por assunto pra não concentrar tudo num arquivo só.
    //
    // Captura: CaptureRegistry (append/update-only) é a fonte única de
    // verdade, então "Refresh limpa o histórico" é estruturalmente
    // impossível. A varredura ativa cobre TMP_Text e UnityEngine.UI.Text
    // legado — captura balão de fala/diálogo in-game que os patches TMP não
    // tocam. Acumulado = Snapshot() do registry; tela atual =
    // RecentlySeen(janela). Chave usa texto stripped + {player} + {n1}/{n2}.
    public sealed partial class TranslationEditorOverlay : MonoBehaviour
    {
        private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            public RectTransform Target = null!;
            private Vector2 _startLocal, _startPtr;
            public void OnBeginDrag(PointerEventData e)
            {
                _startLocal = Target.localPosition;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)Target.parent, e.position, null, out _startPtr);
            }
            public void OnDrag(PointerEventData e)
            {
                if (Target == null) return;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    (RectTransform)Target.parent, e.position, null, out var cur))
                    Target.localPosition = _startLocal + (cur - _startPtr);
            }
        }

        private sealed class EntryRow
        {
            public string     Original    = ""; // chave normalizada (stripped + {player}/{n})
            public string     RawOriginal = ""; // texto bruto com markup (para modal de detalhe)
            public string     Saved       = "";
            public string     Category    = "";
            public string     SourcePath  = "";
            // arquivo real (caminho completo) de onde esta entrada foi lida —
            // vazio = linha do Scan, salva sempre em uncategorized.json;
            // preenchido quando lida de um arquivo real (TranslationIndex),
            // pra Salvar/Ignorar/Del escreverem de volta no lugar certo
            public string     SourceFile  = "";
            // marcação explícita do usuário pro Auto Traduzir — persistida em
            // panel.Selection entre reconstruções da lista (Refresh/DoScan
            // destroem e recriam EntryRow, mas a seleção deve sobreviver)
            public bool       Selected    = false;
            public Image      SelImg      = null!;
            public bool       Alt         = false;
            public Image      RootImg     = null!;
            public Text       OrigLbl     = null!;
            public InputField TransFld    = null!;
            public Button     OkBtn       = null!;
            public GameObject Root        = null!;

            public bool IsTranslated => !string.IsNullOrEmpty(Saved) && Saved != Original;
            public bool IsIgnored    => !string.IsNullOrEmpty(Saved) && Saved == Original;
            public bool IsModified   =>
                TransFld != null
                && !string.IsNullOrWhiteSpace(TransFld.text)
                && TransFld.text != Saved
                && TransFld.text.Trim() != Original.Trim();
        }

        // Tree = árvore de pastas/arquivos, o padrão do F10. Scan continua no
        // enum porque DoScan()/ScanVisibleIntoRegistry ainda são a base da
        // janela standalone "Textos detectados na tela" — a janela principal
        // não usa mais ViewMode.Scan. O Inspetor (F11) foi extraído pra um
        // mod separado.
        private enum ViewMode { Tree, Scan, Settings }

        // Agrupa tudo que uma lista de linhas (busca, filtro, seleção,
        // buffers, rótulos) precisa pra existir de forma independente —
        // permite duas listas simultâneas (a janela principal e a de "Textos
        // detectados na tela"). Os métodos compartilhados (BuildRow/
        // PopulateRows/RefreshVisibility/etc.) recebem um RowListPanel em vez
        // de ler campos fixos da classe — mesma lógica, zero duplicação,
        // operando em qualquer lista independente.
        private sealed class RowListPanel
        {
            public RectTransform ListContent  = null!;
            public InputField    FilterInput  = null!;
            public Text          StatsLabel   = null!;
            public Text?         SaveAllText;
            public GameObject?   ColumnHdr;
            public EntryRowFilter Filter     = null!;
            public SelectionState Selection  = null!;
            public List<EntryRow> Rows       = null!;
            public Dictionary<string, string> Buffers = null!;
            // como esta lista deve se reconstruir após uma ação externa
            // (Mover/Copiar, aplicar Auto Tradução) — cada painel aponta pra
            // seu próprio refresh (DoTree/DoScanWindow/PopulateFileTable)
            public Action RefreshList = () => { };
        }

        private GameObject    _window       = null!;
        private RectTransform _listContent  = null!;
        private InputField    _filterInput  = null!;
        private Text          _statsLabel   = null!;
        private Image?        _imgSettings;

        // qual RowListPanel é dono da sessão atual de Auto Tradução/Mover —
        // os modais são singletons compartilhados entre janelas, guardamos
        // aqui de qual lista as linhas marcadas vieram
        private RowListPanel? _autoPreviewSourcePanel;
        private RowListPanel? _moveSourcePanel;

        // janela standalone "Textos detectados na tela" — estado próprio,
        // independente da janela principal, pra ficar aberta ao mesmo tempo
        private GameObject     _scanWindow          = null!;
        private RectTransform  _scanListContent     = null!;
        private InputField     _scanFilterInput     = null!;
        private Text           _scanStatsLabel      = null!;
        private Text           _scanSaveAllText     = null!;
        private Image          _scanImgAccum        = null!;
        private GameObject     _scanColumnHdr       = null!;
        private RowListPanel   _scanPanel           = null!;

        // uma janela de tabela por arquivo aberto pela árvore
        private readonly Dictionary<string, OpenFileTableWindow> _openFileTables =
            new(StringComparer.OrdinalIgnoreCase);

        private bool  _scanVisible             = false;
        private bool  _scanAccumulate          = false;
        private float _scanAutoRefreshTimer    = 0f;
        private int   _scanWindowLastRegistryCount = 0;
        private readonly HashSet<string> _scanWindowLastSeenTexts = new(StringComparer.Ordinal);

        // Detail modal
        private GameObject  _detailPanel    = null!;
        private InputField  _detailOrigFld  = null!;  // selecionável para copy-paste
        private InputField  _detailTransFld = null!;
        private EntryRow?   _detailRow;
        private RowListPanel? _detailSourcePanel;
        // última posição do cursor no campo de tradução enquanto focado —
        // usada pelos botões de placeholder pra inserir o token no ponto
        // certo (ao clicar num botão o campo perde o foco)
        private int         _lastTransCaret = 0;

        private GameObject   _autoPreviewPanel  = null!;
        private RectTransform _autoPreviewList  = null!;
        private Text          _autoPreviewStats = null!;
        private Text?         _detailAutoBtnText;
        private AutoTranslationOrchestrator? _autoOrchestrator;
        private CancellationTokenSource? _autoCts;
        private bool _autoBusy;
        private readonly List<AutoPreviewRow> _autoPreviewRows = new();
        private const int MaxAutoTranslateBatch = 15;

        private GameObject _movePanel      = null!;
        // escolhido via FilePicker.cs; null = nada escolhido ainda nesta
        // abertura do painel (ver OpenMovePanel)
        private Text       _moveDestLabel  = null!;
        private string?    _moveDestRelPath;
        private Text       _moveHintText   = null!;

        // uma janela de tabela aberta pela árvore, um arquivo por janela,
        // cada uma com seu próprio RowListPanel independente
        private sealed class OpenFileTableWindow
        {
            public string RelativePath = "";
            public string FullPath     = "";
            public GameObject Root     = null!;
            public RowListPanel Panel  = null!;
            // constrói as linhas em lotes (PopulateFileTable, FileTable.cs)
            // pra arquivo grande não travar um frame inteiro — guardado aqui
            // pra poder interromper se a janela fechar no meio
            public Coroutine? PopulateCoroutine;
        }

        private sealed class AutoPreviewRow
        {
            public string     OriginalKey  = "";
            public string     ProposedText = "";
            public string     SourceFile   = ""; // "" = uncategorized.json (Scan)
            public bool       Suspicious   = false; // placeholder não bateu — não aplicável
            public bool       Checked      = false; // aplicar ou não ao confirmar
            public Image      CheckImg     = null!; // botão-chip usado como checkbox (mesmo padrão dos chips de categoria)
            public GameObject Root         = null!;
        }

        private bool    _visible           = false;
        private readonly EntryRowFilter _filter = new(); // busca + categoria + "sem trad."
        private ViewMode _viewMode         = ViewMode.Tree;
        private const float AutoRefreshInterval = 1.0f; // usado pela janela standalone de Scan

        // a captura (pull -> CaptureRegistry) roda sempre por este timer,
        // independente do overlay estar aberto/visível/focado — alimenta o
        // registry mesmo sem nenhuma janela de Scan aberta
        private float _captureTimer = 0f;
        private const float BackgroundCaptureInterval = 1.0f;
        // throttle do log de diagnóstico do scan, evita flood a cada captura
        private int   _lastScanFind = -1, _lastScanKept = -1, _lastScanReg = -1;
        private float _lastScanLogTime = -999f;

        private readonly List<EntryRow>   _rows          = new();
        private readonly Dictionary<string, string> _buffers = new(StringComparer.Ordinal);
        private readonly SelectionState _selection = new();

        private const int MaxEntries = 100;   // limite de linhas no modo "tela atual"
        private const int MaxHistory = 400;   // limite de linhas no modo Acumulado
        // janela do modo "tela atual": entrada com LastSeen dentro deste
        // intervalo. O scan ativo carimba LastSeen=now nos textos visíveis;
        // transientes recém empurrados pelo Harmony também aparecem por alguns segundos
        private const float RecentWindowSeconds = 10f;

        private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);
        private static readonly Color Bg           = C(0.06f, 0.06f, 0.10f, 0.97f);
        private static readonly Color BgHdr        = C(0.10f, 0.10f, 0.17f);
        private static readonly Color BgRow        = C(0.08f, 0.08f, 0.12f);
        private static readonly Color BgRowAlt     = C(0.12f, 0.12f, 0.17f);
        private static readonly Color BgInput      = C(0.15f, 0.15f, 0.22f);
        private static readonly Color BgBtn        = C(0.16f, 0.40f, 0.16f);
        private static readonly Color BgClose      = C(0.50f, 0.12f, 0.12f);
        private static readonly Color BgChip       = C(0.18f, 0.18f, 0.28f);
        private static readonly Color BgChipOn     = C(0.22f, 0.42f, 0.68f);
        private static readonly Color BgIgnBtn     = C(0.22f, 0.22f, 0.36f);
        private static readonly Color BgIgnoredRow = C(0.14f, 0.08f, 0.22f);
        private static readonly Color ColOK        = C(0.30f, 0.90f, 0.40f);
        private static readonly Color ColMiss      = C(1.00f, 0.80f, 0.15f);
        private static readonly Color ColIgnore    = C(0.55f, 0.55f, 0.75f);
        private static readonly Color ColWhite     = Color.white;
        private static readonly Color ColGray      = C(0.55f, 0.55f, 0.60f);

        private static Font? s_font;
        private static Font GetFont()
        {
            if (s_font != null) return s_font;
            s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (s_font == null)
                s_font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return s_font!;
        }

        // "uncategorized.json" — destino de gravação padrão do modo Scan
        // (SourceFile="") e camada de override que sempre vence (ver
        // TranslationRepository.IsOverrideLayer)
        private string OutputFile =>
            Path.Combine(TranslationManager.Instance.TranslationsRootPath, "uncategorized.json");

        private void Awake()
        {
            // BuildUI() fica em EnsureUiBuilt(), chamada só no primeiro F10
            // (ver Update()), não aqui: GameUiAssets resolve sprites reais do
            // jogo via Resources.FindObjectsOfTypeAll<Sprite>(), que só
            // enxerga o que já está carregado em memória naquele instante.
            // Awake() dispara junto com o boot do processo, antes de
            // qualquer tela real aparecer — quase nenhum sprite de UI do
            // jogo estaria carregado ainda. Adiar pro primeiro F10 (evento
            // controlado pelo jogador, tipicamente minutos depois) dá uma
            // chance real de já existir HUD/menu carregado.
        }

        private void EnsureUiBuilt()
        {
            if (_window != null) return;
            try { BuildUI(); Plugin.Log.LogInfo("[AQWTranslation] Overlay v4 pronta (F10)."); }
            catch (Exception ex) { Plugin.Log.LogError($"[AQWTranslation] Overlay erro: {ex}"); }
        }

        private void SetVisible(bool v)
        {
            _visible = v;
            // reindexação sob demanda — dá uma chance de pegar sprite
            // carregado pelo jogo depois da última vez, em vez de ficar
            // preso no que existia na primeira montagem da GUI
            if (v) GameUiAssets.Invalidate();
            _window.SetActive(v);
            InputBlockerPatch.OverlayVisible = v;
            if (!v)
            {
                if (_detailPanel != null) _detailPanel.SetActive(false);
                if (_imgSettings != null) _imgSettings.color = BgChip;
                // nada a limpar: CaptureRegistry é append/update-only e
                // persiste entre aberturas do overlay
            }
            if (v) DoTree(); // reabrir sempre volta pra árvore

            // só dá pra medir RectTransform.rect depois do layout rodar, e o
            // Unity não recalcula layout de objeto inativo — por isso roda
            // daqui (primeira vez que a janela fica ativa), não de dentro de
            // BuildUI() (que roda com _window ainda desativado)
            if (v) LogButtonSizeDiagnosticOnce();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                EnsureUiBuilt();
                SetVisible(!_visible);
            }

            // nada construído ainda (usuário nunca apertou F10 nesta sessão)
            // — só a captura contínua abaixo pode/deve rodar sem a GUI existir
            if (_window != null)
            {
                // rastreia o cursor do campo de tradução enquanto focado — ao
                // clicar num botão de placeholder o foco migra pro botão,
                // então precisamos do último caret válido
                if (_detailPanel != null && _detailPanel.activeSelf
                    && _detailTransFld != null && _detailTransFld.isFocused)
                    _lastTransCaret = _detailTransFld.caretPosition;
            }

            // captura contínua, sempre, desacoplada da exibição: roda mesmo
            // com o overlay fechado e mesmo com um InputField do jogo
            // focado. Só escreve no CaptureRegistry (não mexe na lista da
            // UI), então é seguro rodar a qualquer momento.
            _captureTimer += Time.unscaledDeltaTime;
            if (_captureTimer >= BackgroundCaptureInterval)
            {
                _captureTimer = 0f;
                try { ScanVisibleIntoRegistry(); }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[AQWTranslation] Captura contínua falhou: {ex.Message}");
                }
            }

            // janela standalone "Textos detectados na tela" segue o registry
            // automaticamente — a lista acompanha o crescimento sem depender
            // de clique no Refresh, que o cursor travado do jogo
            // (Cursor.lockState) impede de acertar durante o gameplay.
            // Independente da janela principal, roda mesmo com ela fechada.
            if (_scanVisible)
            {
                _scanAutoRefreshTimer += Time.unscaledDeltaTime;
                if (_scanAutoRefreshTimer >= AutoRefreshInterval)
                {
                    _scanAutoRefreshTimer = 0f;
                    QuickCheckAndRescanScanWindow();
                }
            }
        }
    }
}
