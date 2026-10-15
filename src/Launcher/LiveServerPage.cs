using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>Mode, bots, rules and the buttons that drive the running server. Play stays on quick starts.</summary>
public class LiveServerPage : UserControl
{
    private readonly PlayPage _play;

    public LiveServerPage(AppState app, PlayPage play)
    {
        _play = play;
        BackColor = Theme.Background;
        Padding = Padding.Empty;

        var page = new VStack(
            PlayPage.Header("Live Server", "Set the mode, bots and rules, then run the match while it is up."),
            play.ServerContent);
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        VisibleChanged += (_, _) => { if (Visible) _play.UpdateHackerRow(); };
        app.Changed += () => { if (Visible) _play.UpdateHackerRow(); };
    }
}
