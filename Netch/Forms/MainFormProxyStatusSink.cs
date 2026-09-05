using NekoProxyCore.Core;

namespace Netch.Forms;

/// <summary>
/// Legacy WinForms presentation adapter. It is intentionally outside Core and translates
/// typed lifecycle events into the existing form's state-derived status text.
/// </summary>
internal sealed class MainFormProxyStatusSink : IProxyStatusSink
{
    private readonly MainForm _mainForm;

    public MainFormProxyStatusSink(MainForm mainForm)
    {
        _mainForm = mainForm ?? throw new ArgumentNullException(nameof(mainForm));
    }

    public void OnStatusChanged(ProxyStatusEvent statusEvent)
    {
        if (statusEvent == null)
            throw new ArgumentNullException(nameof(statusEvent));

        _mainForm.StatusText();
    }
}
