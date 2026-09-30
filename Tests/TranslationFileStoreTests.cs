using System;
using System.IO;
using AQWMod.Localization.Repository;
using Xunit;

namespace AQWMod.Localization.Tests
{
    // Usa arquivos reais num diretório temporário isolado por teste — é
    // exatamente o que TranslationFileStore faz em produção (leitura/escrita
    // atômica de UM arquivo JSON), sem precisar de nenhum mock.
    public class TranslationFileStoreTests : IDisposable
    {
        private readonly string _dir;

        public TranslationFileStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aqw_filestore_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        private string PathFor(string name) => Path.Combine(_dir, name);

        [Fact]
        public void Read_NonExistentFile_ReturnsEmptyList()
        {
            var entries = TranslationFileStore.Read(PathFor("missing.json"));
            Assert.Empty(entries);
        }

        [Fact]
        public void Upsert_NewFile_CreatesSimpleStringEntry()
        {
            var file = PathFor("new.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");

            var entries = TranslationFileStore.Read(file);
            var e = Assert.Single(entries);
            Assert.Equal("Attack", e.Key);
            Assert.Equal("Ataque", e.Text);
            Assert.Null(e.Context);
            Assert.False(e.IsIgnored);
        }

        [Fact]
        public void Upsert_WithContext_ProducesExtendedForm()
        {
            var file = PathFor("ctx.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque", context: "skill");

            var e = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("skill", e.Context);
        }

        [Fact]
        public void Upsert_Ignore_StoresOriginalAsText()
        {
            var file = PathFor("ignored.json");
            TranslationFileStore.Upsert(file, "OK", "não deveria importar", ignore: true);

            var e = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("OK", e.Text); // ignore:true => texto = chave original
            Assert.True(e.IsIgnored);
        }

        [Fact]
        public void Upsert_TwiceOnSimpleForm_PreservesSimpleForm_UnlessContextOrIgnoreAdded()
        {
            var file = PathFor("upgrade.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");
            TranslationFileStore.Upsert(file, "Attack", "Ataque!!"); // ainda simples

            var e1 = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("Ataque!!", e1.Text);
            Assert.Null(e1.Context);

            // Agora adiciona contexto — deve migrar pra forma estendida sem
            // perder o texto já salvo.
            TranslationFileStore.Upsert(file, "Attack", "Ataque!!", context: "skill");
            var e2 = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("Ataque!!", e2.Text);
            Assert.Equal("skill", e2.Context);
        }

        [Fact]
        public void Upsert_ExistingExtendedEntry_PreservesFieldsNotBeingChanged()
        {
            var file = PathFor("preserve.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque", context: "skill", source: "manual");

            // Atualiza só o texto — context/source não foram passados de novo,
            // mas o Upsert existente preserva o objeto e só sobrescreve o que
            // foi de fato pedido.
            TranslationFileStore.Upsert(file, "Attack", "Atacar");

            var e = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("Atacar", e.Text);
            Assert.Equal("skill", e.Context); // preservado
        }

        [Fact]
        public void Remove_DeletesEntry_KeepsOthers()
        {
            var file = PathFor("remove.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");
            TranslationFileStore.Upsert(file, "Defend", "Defender");

            TranslationFileStore.Remove(file, "Attack");

            var entries = TranslationFileStore.Read(file);
            var e = Assert.Single(entries);
            Assert.Equal("Defend", e.Key);
        }

        [Fact]
        public void WriteAll_PreservesExtendedFormFieldsForUntouchedKeys()
        {
            // Este é o bug real documentado em TECHNICAL_DEBT.md #17: o leitor
            // de tokens antigo corrompia entradas em forma de objeto ao
            // regravar o arquivo inteiro. TranslationFileStore.WriteAll deve
            // preservar "context" de uma chave que nem foi tocada no dicionário
            // achatado passado.
            var file = PathFor("writeall.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque", context: "skill");

            var flat = new System.Collections.Generic.Dictionary<string, string>
            {
                ["Attack"] = "Ataque", // mesmo valor, não deveria perder o context
                ["Defend"] = "Defender",
            };
            TranslationFileStore.WriteAll(file, flat);

            var entries = TranslationFileStore.Read(file);
            var attack = System.Linq.Enumerable.First(entries, e => e.Key == "Attack");
            Assert.Equal("skill", attack.Context);

            var defend = System.Linq.Enumerable.First(entries, e => e.Key == "Defend");
            Assert.Equal("Defender", defend.Text);
        }

        [Fact]
        public void WriteAll_IdentityValue_MeansIgnoredByLegacyConvention()
        {
            var file = PathFor("legacy_ignore.json");
            var flat = new System.Collections.Generic.Dictionary<string, string> { ["LEVEL"] = "LEVEL" };
            TranslationFileStore.WriteAll(file, flat);

            var e = Assert.Single(TranslationFileStore.Read(file));
            Assert.True(e.IsIgnored);
        }

        [Fact]
        public void WriteAtomic_LeavesNoTempFileBehind()
        {
            var file = PathFor("atomic.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");
            Assert.False(File.Exists(file + ".tmp"));
        }

        [Fact]
        public void WriteAtomic_SecondWrite_CreatesBakOfPreviousVersion()
        {
            var file = PathFor("bak.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");
            TranslationFileStore.Upsert(file, "Attack", "Atacar");

            Assert.True(File.Exists(file + ".bak"));
        }

        [Fact]
        public void EnsureFileExists_NewFile_CreatesEmptyReadableFile()
        {
            var file = PathFor("new_empty.json");
            TranslationFileStore.EnsureFileExists(file);

            Assert.True(File.Exists(file));
            Assert.Empty(TranslationFileStore.Read(file));
        }

        [Fact]
        public void EnsureFileExists_AlreadyExists_DoesNotOverwriteContent()
        {
            var file = PathFor("existing.json");
            TranslationFileStore.Upsert(file, "Attack", "Ataque");

            TranslationFileStore.EnsureFileExists(file);

            var e = Assert.Single(TranslationFileStore.Read(file));
            Assert.Equal("Attack", e.Key);
        }
    }
}
