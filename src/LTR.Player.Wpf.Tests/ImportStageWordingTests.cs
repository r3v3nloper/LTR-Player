using LTR.Catalogue;

namespace LTR.Player.Wpf;

/// <summary>
/// Proves every import stage has wording of its own.
/// </summary>
/// <remarks>
/// Written because one did not. <see cref="SourceImportStage.FetchingVod"/> arrived with the film catalogue,
/// the command line tool was taught to print it and this switch was not — so the longest step of an import
/// on a subscription of sixty thousand films showed "Working..." and nothing else. Driven off the enum so
/// that the next stage added fails here rather than silently reading as the fallback.
/// </remarks>
public sealed class ImportStageWordingTests
{
    private const string Fallback = "Working...";

    public static TheoryData<SourceImportStage> EveryStage =>
        [.. Enum.GetValues<SourceImportStage>()];

    [Theory]
    [MemberData(nameof(EveryStage))]
    public void EveryStage_HasWordingOfItsOwn(SourceImportStage stage)
    {
        // Arrange & Act
        var wording = SourceManagementViewModel.Describe(stage);

        // Assert
        wording.ShouldNotBe(Fallback, $"{stage} is a stage the window can report and has no sentence");
        wording.ShouldEndWith("...", Case.Sensitive, "each is reported while something is still running");
    }

    [Fact]
    public void NoTwoStages_ShareTheirWording()
    {
        // Arrange: two stages reading the same is the other way this goes unnoticed — the status line
        // changes to something identical and the user learns nothing.
        var wordings = Enum.GetValues<SourceImportStage>()
            .Select(SourceManagementViewModel.Describe)
            .ToList();

        // Act & Assert
        wordings.Distinct(StringComparer.Ordinal).Count().ShouldBe(wordings.Count);
    }
}
