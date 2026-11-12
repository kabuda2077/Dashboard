using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Dashboard.Tests;

public sealed class TrayIconThemeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThemeChangesSwitchIconsOnTheUiThreadAndPendingChangesStopAfterDisposal(bool light)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var pending = new ConcurrentQueue<Action>();
                using var tray = new NotifyIcon(); // Never show a tray icon in the user's session.
                using var theme = new TrayIconTheme(tray, pending.Enqueue,
                    Path.Combine(AppContext.BaseDirectory, "resources"), () => light);
                var original = Assert.IsType<Icon>(tray.Icon);
                AssertColor(original, light);

                light = !light;
                theme.OnUserPreferenceChanged(this, new(UserPreferenceCategory.General));
                Assert.Same(original, tray.Icon);
                while (pending.TryDequeue(out var update)) update();
                var replacement = Assert.IsType<Icon>(tray.Icon);
                Assert.NotSame(original, replacement);
                AssertColor(replacement, light);

                light = !light;
                theme.OnUserPreferenceChanged(this, new(UserPreferenceCategory.General));
                while (pending.TryDequeue(out var update)) update();
                Assert.Same(original, tray.Icon); // Reuse icon handles across repeated theme changes.

                light = !light;
                theme.OnUserPreferenceChanged(this, new(UserPreferenceCategory.General));
                theme.Dispose();
                Assert.Null(tray.Icon);
                while (pending.TryDequeue(out var update)) update();
                Assert.Null(tray.Icon);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void AssertColor(Icon icon, bool light)
    {
        using var bitmap = icon.ToBitmap();
        var expected = light ? Color.FromArgb(29, 29, 31) : Color.White;
        var opaquePixels = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A != 255) continue;
            Assert.Equal(expected.ToArgb(), pixel.ToArgb());
            opaquePixels++;
        }
        Assert.True(opaquePixels > 0, "The tray icon must contain a visible foreground.");
    }
}
