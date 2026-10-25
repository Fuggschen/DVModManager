using DV.UIFramework;
using DVModProfiles.Profiles;

namespace DVModProfiles.UI;

internal static class HostProfilePrompt
{
    public const string CHOICES =
        "Always: use the host's settings every time you join.\n" +
        "Once: copy them this time and keep any of your changes in future sessions.\n" +
        "Never: keep your own settings.\n\n" +
        "In every case, settings will be always be restored to their original state when you leave the session.";

    public static Popup Show(PopupManager pm, Popup prefab, string text, Action<HostProfileChoice> answered)
    {
        var keys = new PopupLocalizationKeys
        {
            labelKey = text,
            positiveKey = "Always",
            negativeKey = "Once",
            abortionKey = "Never",
        };

        Popup popup = pm.ShowPopup(prefab, keys, keepLiteralData: true);
        popup.allowEnterKey = false;
        popup.allowEscapeKey = false;

        popup.Closed += result => answered(result.closedBy switch
        {
            PopupClosedByAction.Positive => HostProfileChoice.Always,
            PopupClosedByAction.Negative => HostProfileChoice.Once,
            _ => HostProfileChoice.Never,
        });

        return popup;
    }
}
