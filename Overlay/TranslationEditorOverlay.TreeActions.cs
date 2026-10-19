using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.Localization.Core;
using AQWMod.Localization.Interceptors;

namespace AQWMod.Localization.Overlay
{
    // Ações da árvore sobre arquivos e pastas inteiros: Mover (arquivo ou
    // pasta, destino escolhido no seletor em modo pasta) e Excluir pasta,
    // com confirmação em duas etapas. O trabalho de disco fica em
    // TreeNodeOps.cs; aqui só a GUI e a recarga dos .json afetados.
    public partial class TranslationEditorOverlay
    {
        private GameObject _deleteFolderPanel = null!;
        private Text       _deleteFolderTitle = null!;
        private Text       _deleteFolderLine1 = null!;
        private Text       _deleteFolderLine2 = null!;
        private GameObject _deleteFolderConfirmRow = null!;
        private InputField _deleteFolderInput = null!;
        private Text       _deleteFolderActionText = null!;
        private string     _deleteFolderRelPath = "";
        // false = primeira confirmação; true = segunda (digitar o nome)
        private bool       _deleteFolderSecondStep;

        private void BuildDeleteFolderPanel(RectTransform canvasRoot)
        {
            _deleteFolderPanel = Go("_AQWDeleteFolderPanel", canvasRoot);
            Img(_deleteFolderPanel, Bg);
            var wrt = _deleteFolderPanel.GetComponent<RectTransform>();
            wrt.anchorMin        = new Vector2(0, 1);
            wrt.anchorMax        = new Vector2(0, 1);
            wrt.pivot            = new Vector2(0, 1);
            wrt.anchoredPosition = new Vector2(710, -650);
            wrt.sizeDelta        = new Vector2(520, 160);

            InputBlockerPatch.ExtraBlockedPanels.Add(wrt);

            var vl = _deleteFolderPanel.AddComponent<VerticalLayoutGroup>();
            vl.padding              = new RectOffset(8, 8, 6, 6);
            vl.spacing              = 4;
            vl.childControlWidth    = true;
            vl.childControlHeight   = false; // mesmo tratamento de _movePanel (ver HRow())
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;

            var hdr = HRow(_deleteFolderPanel, 26, BgHdr);
            hdr.AddComponent<DragHandle>().Target = wrt;
            _deleteFolderTitle = Lbl(hdr, "Excluir pasta", 11, FontStyle.Bold, ColWhite, 0, true);
            Btn(hdr, "✕", 26, 22, BgClose, CloseDeleteFolderPanel);

            _deleteFolderLine1 = Lbl(HRow(_deleteFolderPanel, 18, Bg), "", 10, FontStyle.Normal, ColWhite, 0, true);
            _deleteFolderLine2 = Lbl(HRow(_deleteFolderPanel, 18, Bg), "", 9, FontStyle.Italic, ColGray, 0, true);

            _deleteFolderConfirmRow = HRow(_deleteFolderPanel, 24, Bg);
            Lbl(_deleteFolderConfirmRow, "Nome:", 10, FontStyle.Normal, ColGray, 50, false);
            _deleteFolderInput = Fld(_deleteFolderConfirmRow, 0, 22, "digite o nome da pasta", flex: true);

            var foot = HRow(_deleteFolderPanel, 26, BgHdr);
            var actionBtn = Btn(foot, "Sim, continuar", 150, 22, BgClose, ConfirmDeleteFolderStep);
            _deleteFolderActionText = actionBtn.GetComponentInChildren<Text>();
            Btn(foot, "Cancelar", 90, 22, BgIgnBtn, CloseDeleteFolderPanel);

            _deleteFolderPanel.SetActive(false);
        }

        private void OpenDeleteFolderPanel(string folderRelPath)
        {
            var root = TranslationManager.Instance.TranslationsRootPath;
            var (files, folders) = TreeNodeOps.CountContents(root, folderRelPath);

            _deleteFolderRelPath    = folderRelPath;
            _deleteFolderSecondStep = false;
            _deleteFolderTitle.text = $"Excluir pasta {folderRelPath}/";
            _deleteFolderLine1.text = $"Excluir \"{folderRelPath}/\" com {files} arquivo(s) e {folders} subpasta(s)?";
            _deleteFolderLine1.color = ColWhite;
            _deleteFolderLine2.text = $"Ela vai para translations/{TreeNodeOps.TrashFolderName}/ e sai do jogo na hora.";
            _deleteFolderInput.text = "";
            _deleteFolderConfirmRow.SetActive(false);
            _deleteFolderActionText.text = "Sim, continuar";

            _deleteFolderPanel.transform.SetAsLastSibling();
            _deleteFolderPanel.SetActive(true);
        }

        private void CloseDeleteFolderPanel()
        {
            _deleteFolderPanel.SetActive(false);
            _deleteFolderRelPath = "";
        }

        // primeira etapa só confirma a intenção; a segunda exige digitar o
        // nome exato da pasta — um clique duplo sem querer não apaga nada
        private void ConfirmDeleteFolderStep()
        {
            if (_deleteFolderRelPath.Length == 0) { CloseDeleteFolderPanel(); return; }
            var folderName = _deleteFolderRelPath.Split('/').Last();

            if (!_deleteFolderSecondStep)
            {
                _deleteFolderSecondStep = true;
                _deleteFolderLine1.text  = $"Confirmação final: digite \"{folderName}\" e clique Excluir pasta.";
                _deleteFolderLine1.color = ColMiss;
                _deleteFolderConfirmRow.SetActive(true);
                _deleteFolderActionText.text = "Excluir pasta";
                _deleteFolderInput.ActivateInputField();
                return;
            }

            if (!string.Equals(_deleteFolderInput.text.Trim(), folderName, StringComparison.Ordinal))
            {
                _deleteFolderLine2.text = $"O nome digitado não confere com \"{folderName}\".";
                return;
            }

            var relPath = _deleteFolderRelPath;
            var root    = TranslationManager.Instance.TranslationsRootPath;
            try
            {
                CloseFileTablesUnder(relPath, isFolder: true);
                var (removed, trashRel) = TreeNodeOps.TrashFolder(root, relPath, DateTime.Now);
                foreach (var f in removed) TranslationManager.Instance.ReloadFile(f);
                TMPTextPatch.InvalidateSafeTranslateCache();

                _treeExpandedFolders.RemoveWhere(p => p == relPath || p.StartsWith(relPath + "/", StringComparison.Ordinal));
                CloseDeleteFolderPanel();
                DoTree();
                _statsLabel.text = $"Pasta \"{relPath}/\" movida para {trashRel}.";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AQWTranslation] Excluir pasta '{relPath}' falhou: {ex.Message}");
                _deleteFolderLine2.text = "Não foi possível excluir: " + ex.Message;
            }
        }

        // ------------------------------------------------------------ mover

        private void StartMoveNode(string srcRelPath, bool isFolder)
        {
            var what = isFolder ? $"a pasta \"{srcRelPath}/\"" : $"\"{srcRelPath}\"";
            OpenFolderPicker($"Mover {what} para...", destFolder => MoveNode(srcRelPath, isFolder, destFolder));
        }

        private void MoveNode(string srcRelPath, bool isFolder, string destFolderRel)
        {
            var root  = TranslationManager.Instance.TranslationsRootPath;
            var error = TreeNodeOps.ValidateMove(root, srcRelPath, isFolder, destFolderRel, out var destRel);
            if (error != null) { _statsLabel.text = error; return; }

            try
            {
                CloseFileTablesUnder(srcRelPath, isFolder);
                var moved = TreeNodeOps.Move(root, srcRelPath, isFolder, destRel);

                // tira primeiro todos os caminhos antigos da memória, depois
                // carrega os novos — assim nenhuma chave fica "de dono" de um
                // arquivo que já não existe
                foreach (var (oldFull, _) in moved) TranslationManager.Instance.ReloadFile(oldFull);
                foreach (var (_, newFull) in moved) TranslationManager.Instance.ReloadFile(newFull);
                TMPTextPatch.InvalidateSafeTranslateCache();

                if (isFolder)
                {
                    var renamed = _treeExpandedFolders
                        .Where(p => p == srcRelPath || p.StartsWith(srcRelPath + "/", StringComparison.Ordinal))
                        .ToList();
                    foreach (var p in renamed)
                    {
                        _treeExpandedFolders.Remove(p);
                        _treeExpandedFolders.Add(destRel + p.Substring(srcRelPath.Length));
                    }
                }
                if (destFolderRel.Length > 0) _treeExpandedFolders.Add(destFolderRel);

                DoTree();
                _statsLabel.text = $"Movido para {destRel}" + (isFolder ? "/" : "") + ".";
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[AQWTranslation] Mover '{srcRelPath}' falhou: {ex.Message}");
                _statsLabel.text = "Não foi possível mover: " + ex.Message;
            }
        }

        // janela de tabela aberta pra um arquivo que vai mudar de lugar ou
        // sumir apontaria pro caminho velho — fecha antes de mexer no disco
        private void CloseFileTablesUnder(string relPath, bool isFolder)
        {
            var affected = _openFileTables.Keys
                .Where(k => isFolder
                    ? k.StartsWith(relPath + "/", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(k, relPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in affected) CloseFileTable(k);
        }
    }
}
