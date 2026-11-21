using Akira.Core.Library;
using Xunit;

namespace Akira.Core.Tests.Library;

public class TitleLettersTests
{
    [Fact]
    public void Index_GroupsByFirstLetter_WithDigitsUnderHashAndAccentsUnderTheirLetter()
    {
        var index = TitleLetters.Index(["100% wolf", "alien", "écran", "heat", "heist"]);

        Assert.Equal([("#", 0, 1), ("A", 1, 1), ("E", 2, 1), ("H", 3, 2)], index);
    }

    [Fact]
    public void Fold_DropsAccentsAndCase_SoSearchMatchesEitherSpelling()
    {
        Assert.Equal("amelie", SearchText.Fold("Amélie"));
        Assert.Equal("pokemon: the movie", SearchText.Fold("Pokémon: The Movie"));
    }
}
