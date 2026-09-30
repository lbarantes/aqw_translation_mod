using AQWMod.Localization.Core;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class TranslationKeyTests
    {
        [Fact]
        public void SameTextNoContext_AreEqual()
        {
            var a = new TranslationKey("Attack");
            var b = new TranslationKey("Attack");
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void EmptyContext_IsNormalizedToNull_SameAsNoContext()
        {
            // Contrato do construtor: string.IsNullOrEmpty(context) => null.
            // Isso é o que garante que uma chave "sem contexto" sempre bata
            // com o fallback bare-text do repositório, não importa como foi
            // construída.
            var a = new TranslationKey("Attack", "");
            var b = new TranslationKey("Attack");
            Assert.Equal(a, b);
            Assert.Null(a.Context);
        }

        [Fact]
        public void SameTextDifferentContext_AreNotEqual()
        {
            var skill  = new TranslationKey("Attack", "skill");
            var button = new TranslationKey("Attack", "button");
            var bare   = new TranslationKey("Attack");

            Assert.NotEqual(skill, button);
            Assert.NotEqual(skill, bare);
            Assert.NotEqual(button, bare);
        }

        [Fact]
        public void Equality_IsCaseSensitive_Ordinal()
        {
            // Duas strings-fonte diferentes por capitalização são identidades
            // DIFERENTES — o repositório não deve fundi-las silenciosamente.
            Assert.NotEqual(new TranslationKey("attack"), new TranslationKey("Attack"));
        }

        [Fact]
        public void ToString_WithoutContext_IsJustTheText()
        {
            Assert.Equal("Attack", new TranslationKey("Attack").ToString());
        }

        [Fact]
        public void ToString_WithContext_UsesDoubleAtSeparator()
        {
            Assert.Equal("Attack@@skill", new TranslationKey("Attack", "skill").ToString());
        }
    }
}
