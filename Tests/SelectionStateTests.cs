using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class SelectionStateTests
    {
        [Fact]
        public void NewState_ContainsNothing()
        {
            var sel = new SelectionState();
            Assert.False(sel.Contains("ui/hud.json", "Attack"));
        }

        [Fact]
        public void AddThenContains_ReturnsTrue()
        {
            var sel = new SelectionState();
            sel.Add("ui/hud.json", "Attack");
            Assert.True(sel.Contains("ui/hud.json", "Attack"));
        }

        [Fact]
        public void SameOriginal_DifferentSourceFile_AreDistinctSelections()
        {
            // Motivo de existir: a mesma chave de texto pode vir de arquivos
            // diferentes (Biblioteca navega o corpus inteiro) — selecionar
            // uma não pode marcar a outra sem querer.
            var sel = new SelectionState();
            sel.Add("ui/hud.json", "Attack");

            Assert.True(sel.Contains("ui/hud.json", "Attack"));
            Assert.False(sel.Contains("guis/skills.json", "Attack"));
        }

        [Fact]
        public void Remove_ClearsOnlyThatKey()
        {
            var sel = new SelectionState();
            sel.Add("ui/hud.json", "Attack");
            sel.Add("ui/hud.json", "Defend");

            sel.Remove("ui/hud.json", "Attack");

            Assert.False(sel.Contains("ui/hud.json", "Attack"));
            Assert.True(sel.Contains("ui/hud.json", "Defend"));
        }

        [Fact]
        public void Remove_NonExistentKey_IsNoOp()
        {
            var sel = new SelectionState();
            sel.Remove("ui/hud.json", "DoesNotExist"); // não deve lançar
        }

        [Fact]
        public void Clear_RemovesAllSelections()
        {
            var sel = new SelectionState();
            sel.Add("ui/hud.json", "Attack");
            sel.Add("guis/skills.json", "Defend");

            sel.Clear();

            Assert.False(sel.Contains("ui/hud.json", "Attack"));
            Assert.False(sel.Contains("guis/skills.json", "Defend"));
        }

        [Fact]
        public void AddTwice_IsIdempotent()
        {
            var sel = new SelectionState();
            sel.Add("ui/hud.json", "Attack");
            sel.Add("ui/hud.json", "Attack");
            Assert.True(sel.Contains("ui/hud.json", "Attack"));

            sel.Remove("ui/hud.json", "Attack");
            Assert.False(sel.Contains("ui/hud.json", "Attack"));
        }
    }
}
