using LongBetterWindows.Host.Interaction;

namespace LongBetterWindows.Tests;

public sealed class SettingsCategoryAccessibilityProjectionTests
{
    [Theory]
    [InlineData(true, "已选中，第 2 项，共 4 项")]
    [InlineData(false, "未选中，第 2 项，共 4 项")]
    public void Build_ProjectsLocalizedSelectionAndSetPosition(
        bool isSelected,
        string expectedStatus)
    {
        var state = SettingsCategoryAccessibilityProjection.Build(
            "交互与面板",
            isSelected,
            2,
            4,
            key => key switch
            {
                "settings.category.state.selected" => "已选中，第 {0} 项，共 {1} 项",
                "settings.category.state.notSelected" => "未选中，第 {0} 项，共 {1} 项",
                _ => key,
            });

        Assert.Equal("交互与面板", state.Name);
        Assert.Equal(expectedStatus, state.ItemStatus);
        Assert.Equal(2, state.PositionInSet);
        Assert.Equal(4, state.SizeOfSet);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(5, 4)]
    [InlineData(1, 0)]
    public void Build_RejectsInvalidSetPosition(int position, int size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SettingsCategoryAccessibilityProjection.Build(
                "Updates",
                false,
                position,
                size,
                key => key));
    }
}
