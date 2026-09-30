using System;
using System.IO;
using AQWMod.Localization.Debug;
using AQWMod.Localization.Repository;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // Precisa do LogBootstrap (TranslationLogger exige Plugin.Log, que só
    // existe de verdade dentro do jogo via BepInEx) — ver TestBootstrap.cs.
    [Collection("LogBootstrap")]
    public class TranslationRepositoryTests : IDisposable
    {
        private readonly string _dir;

        public TranslationRepositoryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aqw_repo_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        private void WriteJson(string relativePath, string json)
        {
            var full = Path.Combine(_dir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, json);
        }

        private static TranslationRepository NewRepo() => new TranslationRepository(new TranslationLogger());

        [Fact]
        public void LoadAll_SimpleFile_IsFoundByTryGet()
        {
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque\" }");
            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", out var value));
            Assert.Equal("Ataque", value);
        }

        [Fact]
        public void LoadAll_MissingKey_TryGetReturnsFalse()
        {
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque\" }");
            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.False(repo.TryGet("NotThere", out _));
        }

        [Fact]
        public void LoadAll_OverridesFolder_AlwaysWinsOverContent()
        {
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque (conteudo)\" }");
            WriteJson("overrides/manual.json", "{ \"Attack\": \"Ataque (override)\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", out var value));
            Assert.Equal("Ataque (override)", value);
        }

        [Fact]
        public void LoadAll_UncategorizedJsonAtRoot_ActsAsOverrideLayer()
        {
            // Documentado em ADR-0002/MIGRATION.md #5 (renomeado de
            // "ingame_edits.json" para "uncategorized.json" em 2026-09-23,
            // mesmo papel de camada de override na raiz).
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque (conteudo)\" }");
            WriteJson("uncategorized.json", "{ \"Attack\": \"Ataque (edicao manual)\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", out var value));
            Assert.Equal("Ataque (edicao manual)", value);
        }

        [Fact]
        public void LoadAll_ConflictWithinContentLayer_FirstFileAlphabetically_Wins()
        {
            // "a_first.json" < "z_second.json" em ordem alfabética — a
            // primeira definição vence, e o carregamento NÃO deve lançar
            // (o conflito vira um log de warning, não um erro fatal).
            WriteJson("a_first.json", "{ \"Attack\": \"Primeira\" }");
            WriteJson("z_second.json", "{ \"Attack\": \"Segunda\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", out var value));
            Assert.Equal("Primeira", value);
        }

        [Fact]
        public void LoadAll_SameValueInTwoFiles_IsNotTreatedAsConflict()
        {
            // Não é um conflito de verdade se as duas fontes concordam no
            // valor — só diverge quando os TEXTOS diferem.
            WriteJson("a.json", "{ \"OK\": \"OK\" }");
            WriteJson("b.json", "{ \"OK\": \"OK\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir); // não deve lançar
            Assert.True(repo.TryGet("OK", out var value));
            Assert.Equal("OK", value);
        }

        [Fact]
        public void LoadAll_NpcNamesFileAtRoot_IsExcludedFromTranslations()
        {
            // Bug latente real documentado em TECHNICAL_DEBT.md #14 — este
            // arquivo é uma tabela de nomes de NPC, não deve virar tradução
            // de texto solto.
            WriteJson("npc_names.json", "{ \"Booker\": \"Bibliotecario\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.False(repo.TryGet("Booker", out _));
        }

        [Fact]
        public void LoadAll_FilesUnderUnderscoreSegment_AreExcluded()
        {
            // "_capture/dialogue.json" — TECHNICAL_DEBT.md: o filtro antigo só
            // olhava o NOME do arquivo, não qualquer segmento do caminho.
            WriteJson("_capture/dialogue.json", "{ \"Secret\": \"Segredo\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.False(repo.TryGet("Secret", out _));
        }

        [Fact]
        public void LoadAll_IdentityValue_IsStoredAsIgnored_NotDiscarded()
        {
            // TECHNICAL_DEBT.md #16: entradas "X":"X" antes eram descartadas
            // ao carregar — precisam ser encontradas (para não virar "faltando
            // tradução" infinito no MissingTranslationCollector).
            WriteJson("ui/hud.json", "{ \"LEVEL\": \"LEVEL\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("LEVEL", out var value));
            Assert.Equal("LEVEL", value);
        }

        [Fact]
        public void LoadAll_ExtendedForm_WithContext_IsReachableViaContextQualifiedLookup()
        {
            WriteJson("guis/skills.json",
                "{ \"translations\": { \"Attack\": { \"text\": \"Ataque (skill)\", \"context\": \"skill\" } } }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", "skill", out var withContext));
            Assert.Equal("Ataque (skill)", withContext);
        }

        [Fact]
        public void TryGet_ContextQualifiedLookup_FallsBackToBareText_WhenNoContextEntryExists()
        {
            // O contrato de segurança central do ADR-0001: procurar com
            // contexto NUNCA falha "de verdade" se existir uma entrada
            // sem contexto — sempre cai de volta pro texto puro.
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.True(repo.TryGet("Attack", "some-context-with-no-entry", out var value));
            Assert.Equal("Ataque", value);
        }

        [Fact]
        public void ReloadFile_UpdatesOnlyThatFilesKeys()
        {
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque\" }");
            WriteJson("ui/other.json", "{ \"Defend\": \"Defender\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);

            WriteJson("ui/hud.json", "{ \"Attack\": \"Atacar\" }");
            repo.ReloadFile(Path.Combine(_dir, "ui/hud.json"));

            Assert.True(repo.TryGet("Attack", out var attack));
            Assert.Equal("Atacar", attack);
            Assert.True(repo.TryGet("Defend", out var defend)); // arquivo não tocado, intacto
            Assert.Equal("Defender", defend);
        }

        [Fact]
        public void ReloadFile_KeyRemovedFromDisk_IsAlsoRemovedFromMemory()
        {
            // Bug real reportado pelo usuário: o botão "Del" apagava a chave
            // do arquivo em disco corretamente, mas ReloadFile só sabia
            // ADICIONAR/ATUALIZAR chaves presentes no arquivo — nunca
            // detectava uma chave que tinha sumido. Resultado: a tradução
            // "fantasma" continuava resolvendo em memória (GUI mostrava
            // [T]/verde) até reiniciar o jogo (que sempre recarrega do zero).
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque\", \"Defend\": \"Defender\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);
            Assert.True(repo.TryGet("Attack", out _));

            // Reescreve o arquivo SEM "Attack" (simula TranslationFileStore.Remove).
            WriteJson("ui/hud.json", "{ \"Defend\": \"Defender\" }");
            repo.ReloadFile(Path.Combine(_dir, "ui/hud.json"));

            Assert.False(repo.TryGet("Attack", out _)); // removida de verdade da memória
            Assert.True(repo.TryGet("Defend", out var defend)); // chave que ficou continua intacta
            Assert.Equal("Defender", defend);
        }

        [Fact]
        public void ReloadFile_KeyRemovedFromDisk_ButOverriddenElsewhere_KeepsTheOverride()
        {
            // Se outra camada (override) já venceu para essa chave, remover
            // do arquivo de conteúdo NÃO deve apagar a versão que está
            // vencendo agora — só quem "é dono" da chave em memória pode
            // removê-la nesta recarga.
            WriteJson("ui/hud.json", "{ \"Attack\": \"Ataque (conteudo)\" }");
            WriteJson("overrides/manual.json", "{ \"Attack\": \"Ataque (override)\" }");

            var repo = NewRepo();
            repo.LoadAll(_dir);
            Assert.True(repo.TryGet("Attack", out var beforeValue));
            Assert.Equal("Ataque (override)", beforeValue); // override venceu no LoadAll

            WriteJson("ui/hud.json", "{}"); // remove "Attack" do arquivo de conteúdo
            repo.ReloadFile(Path.Combine(_dir, "ui/hud.json"));

            Assert.True(repo.TryGet("Attack", out var afterValue));
            Assert.Equal("Ataque (override)", afterValue); // override intacto
        }

        [Fact]
        public void Count_ReflectsTotalMergedEntries()
        {
            WriteJson("a.json", "{ \"One\": \"Um\", \"Two\": \"Dois\" }");
            var repo = NewRepo();
            repo.LoadAll(_dir);

            Assert.Equal(2, repo.Count);
        }

        [Fact]
        public void LoadAll_NonExistentDirectory_DoesNotThrow_AndLeavesEmptyRepository()
        {
            var repo = NewRepo();
            repo.LoadAll(Path.Combine(_dir, "does_not_exist"));
            Assert.Equal(0, repo.Count);
        }
    }
}
