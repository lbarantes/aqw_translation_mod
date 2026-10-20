using AQWMod.Localization.Utils;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class PlayerTemplateIndexTests
    {
        private static PlayerTemplateIndex Index(params string[] keys)
        {
            var idx = new PlayerTemplateIndex();
            idx.Rebuild(keys);
            return idx;
        }

        [Fact]
        public void NameWithoutUnderscore_MatchesTemplate()
        {
            var idx = Index("{player} has invited you to join a party!");
            Assert.True(idx.TryMatch("pittureca has invited you to join a party!", out var m));
            Assert.Equal("{player} has invited you to join a party!", m.TemplateKey);
            Assert.Equal(new[] { "pittureca" }, m.Players);
            Assert.Equal("pittureca te convidou para entrar no grupo!",
                m.Fill("{player} te convidou para entrar no grupo!"));
        }

        [Fact]
        public void NameWithSpaces_IsCaptured()
        {
            var idx = Index("{player} wants to summon you.");
            Assert.True(idx.TryMatch("Hero Of Lore wants to summon you.", out var m));
            Assert.Equal(new[] { "Hero Of Lore" }, m.Players);
        }

        [Fact]
        public void PlayerInMiddle_AndNumbers()
        {
            var idx = Index("Party: {player} gained {n1} gold");
            Assert.True(idx.TryMatch("Party: xow gained 150 gold", out var m));
            Assert.Equal(new[] { "xow" }, m.Players);
            Assert.Equal(new[] { "150" }, m.Numbers);
            Assert.Equal("Grupo: xow ganhou 150 de ouro", m.Fill("Grupo: {player} ganhou {n1} de ouro"));
        }

        [Fact]
        public void TwoPlayers()
        {
            var idx = Index("{player} defeated {player2}!");
            Assert.True(idx.TryMatch("ana defeated bob!", out var m));
            Assert.Equal(new[] { "ana", "bob" }, m.Players);
            Assert.Equal("bob perdeu para ana!", m.Fill("{player2} perdeu para {player}!"));
        }

        [Fact]
        public void TextOutsideTemplate_DoesNotMatch()
        {
            var idx = Index("{player} has invited you to join a party!");
            Assert.False(idx.TryMatch("pittureca has left the party.", out _));
            Assert.False(idx.TryMatch("has invited you to join a party!", out _));
        }

        [Fact]
        public void NameLongerThanLimit_DoesNotMatch()
        {
            var idx = Index("Hello {player}!");
            Assert.False(idx.TryMatch("Hello " + new string('a', PlayerTemplateIndex.MaxNameLength + 1) + "!", out _));
        }

        [Fact]
        public void NameAcrossLineBreak_DoesNotMatch()
        {
            var idx = Index("Hello {player}!");
            Assert.False(idx.TryMatch("Hello a\nb!", out _));
        }

        [Fact]
        public void KeysWithoutPlayer_OrTooLittleText_AreIgnored()
        {
            var idx = Index("Plain text", "{n1} gold", "{player}!", "{player}{player2} hi");
            Assert.Equal(0, idx.Count);
        }

        [Fact]
        public void ContextSuffix_IsStripped()
        {
            var idx = Index("{player} joined.@@chat");
            Assert.True(idx.TryMatch("bob joined.", out var m));
            Assert.Equal("{player} joined.", m.TemplateKey);
        }

        [Fact]
        public void MoreSpecificTemplate_Wins()
        {
            var idx = Index("{player} says hi", "{player} says hi to the party");
            Assert.True(idx.TryMatch("bob says hi to the party", out var m));
            Assert.Equal("{player} says hi to the party", m.TemplateKey);
            Assert.Equal(new[] { "bob" }, m.Players);
        }
    }
}
