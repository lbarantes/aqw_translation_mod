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
    // Modal "Nova pasta"/"Novo arquivo", acionado pelos botões de cabeçalho
    // (raiz de translations/) e pelo dropdown "+" de cada linha de pasta na
    // árvore. Painel único e compartilhado — o modo (pasta ou arquivo) e a
    // pasta-pai alvo ficam em campos de estado.
    public partial class TranslationEditorOverlay
    {
        private GameObject _createNodePanel     = null!;
        private Text       _createNodeTitle     = null!;
        private InputField _createNodeInput     = null!;
        private Text       _createNodeHintText  = null!;
        private bool       _createNodeIsFolder;
        private string     _createNodeParentRelPath = "";
        // callback opcional (relPath, isFolder) chamado ao criar com sucesso,
        // em vez do padrão (DoTree() e OpenFileTable() pra arquivo novo) —
        // usado pelo seletor de arquivo (FilePicker.cs), que reage diferente
        private Action<string, bool>? _createNodeOnCreated;

        private void BuildCreateNodePanel(RectTransform canvasRoot)
        {
            _createNodePanel = Go("_AQWCreateNodePanel", canvasRoot);
            Img(_createNodePanel, Bg);
            var wrt = _createNodePanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(710, -650);
            wrt.sizeDelta        = new Vector2(460, 150);

            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _createNodePanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(8, 8, 6, 6);
            vl.spacing              = 4;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // mesmo tratamento de _movePanel (ver HRow())
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var hdr = HRow(_createNodePanel, 26, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            _createNodeTitle = Lbl(hdr, "Nova pasta", 11, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 26, 22, BgClose, () => _createNodePanel.SetActive(false));

            var nameRow = HRow(_createNodePanel, 24, Bg);
            Lbl(nameRow, "Nome:", 10, FontStyle.Normal, ColGray, 50, false);
            _createNodeInput = Fld(nameRow, 0, 22, "ex.: npcs/Alina.json", flex: true);

            var hintRow = HRow(_createNodePanel, 34, Bg);
            _createNodeHintText = Lbl(hintRow, "", 9, FontStyle.Italic, ColGray, 0, true);

            var foot = HRow(_createNodePanel, 26, BgHdr);
            Btn(foot, "Criar",    80, 22, BgBtn,   () => ConfirmCreateNode());
            Btn(foot, "Cancelar", 90, 22, BgClose, () => _createNodePanel.SetActive(false));

            _createNodePanel.SetActive(false);
        }

        private void OpenCreateNodePanel(string parentRelPath, bool isFolder, Action<string, bool>? onCreated = null)
        {
            _createNodeParentRelPath = parentRelPath;
            _createNodeIsFolder      = isFolder;
            _createNodeOnCreated     = onCreated;

            var where = string.IsNullOrEmpty(parentRelPath) ? "translations/" : $"{parentRelPath}/";
            _createNodeTitle.text    = isFolder ? $"Nova pasta em {where}" : $"Novo arquivo em {where}";
            _createNodeInput.text    = "";
            _createNodeHintText.text = isFolder
                ? "Pode incluir subpastas (ex.: \"npcs/quest\")."
                : "\".json\" é adicionado automaticamente se você não digitar.";

            _createNodePanel.SetActive(true);
            _createNodeInput.ActivateInputField();
        }

        // Dropdown "+" das linhas de pasta da árvore: painel único e
        // reaproveitado — cada clique reposiciona o mesmo GameObject perto
        // do botão "+" clicado, em vez de criar um dropdown por linha (a
        // árvore inteira é destruída/reconstruída a cada DoTree(), então
        // manter estado por linha não faria sentido). "Clicar fora fecha" via
        // um catcher invisível cobrindo a tela inteira, sempre um sibling
        // antes do dropdown (clique dentro acerta o menu, fora acerta o
        // catcher, que só fecha).
        private GameObject _createNodeDropdown          = null!;
        private GameObject _createNodeDropdownCatcher    = null!;
        private string     _createNodeDropdownParentPath = "";

        private void BuildCreateNodeDropdown(RectTransform canvasRoot)
        {
            _createNodeDropdownCatcher = Go("_AQWCreateNodeDropdownCatcher", canvasRoot);
            var catcherRt = _createNodeDropdownCatcher.GetComponent<RectTransform>();
            catcherRt.anchorMin = Vector2.zero; catcherRt.anchorMax = Vector2.one;
            catcherRt.offsetMin = catcherRt.offsetMax = Vector2.zero;
            Img(_createNodeDropdownCatcher, new Color(0, 0, 0, 0.001f)); // ~invisível, mas ainda bloqueia raycast
            _createNodeDropdownCatcher.AddComponent<Button>().onClick.AddListener(CloseCreateNodeDropdown);
            InputBlockerPatch.ExtraBlockedPanels.Add(catcherRt);
            _createNodeDropdownCatcher.SetActive(false);

            _createNodeDropdown = Go("_AQWCreateNodeDropdown", canvasRoot);
            Img(_createNodeDropdown, Bg);
            var ddRt = _createNodeDropdown.GetComponent<RectTransform>();
            ddRt.anchorMin = new Vector2(0, 1);
            ddRt.anchorMax = new Vector2(0, 1);
            ddRt.pivot     = new Vector2(0, 1);
            ddRt.sizeDelta = new Vector2(108, 48);
            InputBlockerPatch.ExtraBlockedPanels.Add(ddRt);

            var vl = _createNodeDropdown.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(3, 3, 3, 3);
            vl.spacing              = 2;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // ver nota de sizeDelta em HRow()
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            // Btn() sozinho não fixa a própria altura via RectTransform — só
            // funciona nos outros call sites porque são filhos de um HRow com
            // childControlHeight=true, que dimensiona o filho de verdade.
            // Aqui os botões são filhos diretos do VerticalLayoutGroup acima
            // (childControlHeight=false), então precisam de BakeExplicitHeight
            // pra não ficar com altura indefinida (mesma classe de bug do
            // cabeçalho, ver HRow()).
            var newFolderBtn = Btn(_createNodeDropdown, "Nova pasta", 100, 20, BgChip, () =>
            {
                OpenCreateNodePanel(_createNodeDropdownParentPath, isFolder: true, _createNodeDropdownOnCreated);
                CloseCreateNodeDropdown();
            });
            BakeExplicitHeight(newFolderBtn.GetComponent<RectTransform>(), MinButtonHeight);

            var newFileBtn = Btn(_createNodeDropdown, "Novo arquivo", 100, 20, BgChip, () =>
            {
                OpenCreateNodePanel(_createNodeDropdownParentPath, isFolder: false, _createNodeDropdownOnCreated);
                CloseCreateNodeDropdown();
            });
            BakeExplicitHeight(newFileBtn.GetComponent<RectTransform>(), MinButtonHeight);

            _createNodeDropdown.SetActive(false);
        }

        private Action<string, bool>? _createNodeDropdownOnCreated;

        private void OpenCreateNodeDropdown(string parentRelPath, RectTransform anchorButton, Action<string, bool>? onCreated = null)
        {
            _createNodeDropdownParentPath  = parentRelPath;
            _createNodeDropdownOnCreated   = onCreated;

            // posiciona o dropdown logo abaixo do botão "+" clicado: converte
            // o canto inferior-esquerdo do botão (espaço de mundo) pra
            // coordenada local do canvasRoot e daí pra anchoredPosition
            // relativo ao canto superior-esquerdo, mesma convenção de todo
            // painel flutuante do mod
            var canvasRoot = _createNodeDropdown.transform.parent as RectTransform;
            var corners = new Vector3[4];
            anchorButton.GetWorldCorners(corners); // 0=inferior-esq, 1=superior-esq, 2=superior-dir, 3=inferior-dir
            var screenPoint = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screenPoint, null, out var local);

            var ddRt = _createNodeDropdown.GetComponent<RectTransform>();
            float halfW = canvasRoot!.rect.width  * 0.5f;
            float halfH = canvasRoot.rect.height * 0.5f;
            ddRt.anchoredPosition = new Vector2(local.x + halfW, local.y - halfH);

            _createNodeDropdownCatcher.transform.SetAsLastSibling();
            _createNodeDropdown.transform.SetAsLastSibling();
            _createNodeDropdownCatcher.SetActive(true);
            _createNodeDropdown.SetActive(true);
        }

        private void CloseCreateNodeDropdown()
        {
            _createNodeDropdown.SetActive(false);
            _createNodeDropdownCatcher.SetActive(false);
        }

        private void ConfirmCreateNode()
        {
            var root = TranslationManager.Instance.TranslationsRootPath;

            if (_createNodeIsFolder)
            {
                var error = NewNodeNameValidator.ValidateFolderPath(
                    _createNodeInput.text, _createNodeParentRelPath, out var relPath);
                if (error != null) { _createNodeHintText.text = error; return; }

                var fullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(fullPath)) { _createNodeHintText.text = "Essa pasta já existe."; return; }
                // sem isto, Directory.CreateDirectory jogaria exceção não
                // capturada quando o path já é ocupado por um arquivo
                if (File.Exists(fullPath)) { _createNodeHintText.text = "Já existe um ARQUIVO com esse nome."; return; }

                Directory.CreateDirectory(fullPath);
                if (_createNodeParentRelPath.Length > 0) _treeExpandedFolders.Add(_createNodeParentRelPath);
                _treeExpandedFolders.Add(relPath);

                _createNodePanel.SetActive(false);
                if (_createNodeOnCreated != null) _createNodeOnCreated(relPath, true);
                else DoTree();
            }
            else
            {
                var error = NewNodeNameValidator.ValidateFileName(
                    _createNodeInput.text, _createNodeParentRelPath, out var relPath);
                if (error != null) { _createNodeHintText.text = error; return; }

                var fullPath = Path.Combine(root, relPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath)) { _createNodeHintText.text = "Esse arquivo já existe."; return; }
                // File.Exists é false pra um caminho que é pasta, então sem
                // esta checagem passava direto pra EnsureFileExists, que
                // jogava exceção não capturada na escrita atômica (tentando
                // mover pro lugar de um diretório existente)
                if (Directory.Exists(fullPath)) { _createNodeHintText.text = "Já existe uma PASTA com esse nome."; return; }

                TranslationFileStore.EnsureFileExists(fullPath);
                TranslationManager.Instance.ReloadFile(fullPath);
                if (_createNodeParentRelPath.Length > 0) _treeExpandedFolders.Add(_createNodeParentRelPath);

                _createNodePanel.SetActive(false);
                if (_createNodeOnCreated != null) _createNodeOnCreated(relPath, false);
                else { DoTree(); OpenFileTable(relPath); }
            }
        }
    }
}
