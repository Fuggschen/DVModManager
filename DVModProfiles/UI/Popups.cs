using DV.UI;
using DV.UIFramework;
using UnityEngine;

namespace DVModProfiles.UI;

internal static class Popups
{
    private static PopupManager? popupManager;

    // The popup manager for the UI showing right now, and the game's three-button popup. With
    // takeOver, a popup already open is closed to make room rather than waiting on it.
    public static bool TryGetThreeButton(Behaviour from, out PopupManager pm, out Popup prefab, bool takeOver = false)
    {
        pm = from.FindPopupManager(ref popupManager);
        if (pm == null)
        {
            pm = UnityEngine.Object.FindObjectOfType<PopupManager>();
        }

        Popup? found = Resources.FindObjectsOfTypeAll<PopupNotificationReferences>()
            .Select(references => references.popup3Buttons)
            .FirstOrDefault(popup => popup != null && popup.abortionButton != null);

        prefab = found!;
        if (pm == null || found == null)
        {
            return false;
        }

        if (takeOver && !pm.CanShowPopup())
        {
            pm.CloseActivePopup();
        }

        return pm.CanShowPopup();
    }
}
