using System.Windows.Forms;
using NovaCraft.UI;

namespace NovaCraft.Tests;

public sealed class AnimatedNavigationButtonTests
{
    [Fact]
    public void SelectionMarkerTransitionsInAndOut()
    {
        using var button = new AnimatedNavigationButton
        {
            Size = new Size(214, 40),
            Text = "Главная"
        };

        Assert.Equal(0f, button.SelectionProgress);

        button.Selected = true;
        button.AdvanceAnimationFrame(1f);

        Assert.Equal(1f, button.SelectionProgress);
        Assert.NotEqual(Color.FromArgb(15, 17, 27), button.BackColor);

        button.Selected = false;
        button.AdvanceAnimationFrame(1f);

        Assert.Equal(0f, button.SelectionProgress);
        Assert.Equal(Color.FromArgb(15, 17, 27), button.BackColor);
    }

    [Fact]
    public void NavigationButtonRemainsKeyboardAccessible()
    {
        using var button = new AnimatedNavigationButton
        {
            Size = new Size(214, 40),
            Text = "Моды"
        };

        Assert.Equal(AccessibleRole.PushButton, button.AccessibleRole);
        Assert.True(button.TabStop);
        Assert.Equal("Моды", button.Text);
    }
}
