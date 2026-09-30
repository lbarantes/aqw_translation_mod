using AQWMod.Localization.Overlay;
using Xunit;

namespace AQWMod.Localization.Tests
{
    public class NewNodeNameValidatorTests
    {
        // ValidateFolderPath

        [Fact]
        public void ValidateFolderPath_SimpleNameAtRoot_ReturnsRelativePath()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("npcs", "", out var relPath);

            Assert.Null(error);
            Assert.Equal("npcs", relPath);
        }

        [Fact]
        public void ValidateFolderPath_WithParent_CombinesPaths()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("quest", "npcs", out var relPath);

            Assert.Null(error);
            Assert.Equal("npcs/quest", relPath);
        }

        [Fact]
        public void ValidateFolderPath_NestedSubfolders_NormalizesSlashes()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("a\\b", "", out var relPath);

            Assert.Null(error);
            Assert.Equal("a/b", relPath);
        }

        [Fact]
        public void ValidateFolderPath_Empty_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("   ", "", out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateFolderPath_ParentTraversal_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("../outside", "", out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateFolderPath_AbsolutePath_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("C:\\Windows", "", out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateFolderPath_InvalidChars_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFolderPath("bad:name", "", out _);

            Assert.NotNull(error);
        }

        // ValidateFileName

        [Fact]
        public void ValidateFileName_WithoutExtension_AddsJson()
        {
            var error = NewNodeNameValidator.ValidateFileName("Alina", "npcs", out var relPath);

            Assert.Null(error);
            Assert.Equal("npcs/Alina.json", relPath);
        }

        [Fact]
        public void ValidateFileName_WithExtension_KeepsAsIs()
        {
            var error = NewNodeNameValidator.ValidateFileName("Alina.json", "", out var relPath);

            Assert.Null(error);
            Assert.Equal("Alina.json", relPath);
        }

        [Fact]
        public void ValidateFileName_ContainsSlash_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFileName("sub/Alina.json", "", out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateFileName_Empty_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFileName("", "npcs", out _);

            Assert.NotNull(error);
        }

        [Fact]
        public void ValidateFileName_ParentTraversal_ReturnsError()
        {
            var error = NewNodeNameValidator.ValidateFileName("..json", "", out _);

            Assert.NotNull(error);
        }
    }
}
