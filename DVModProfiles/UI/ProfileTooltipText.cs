using DV.UI;

namespace DVModProfiles.UI;

public class ProfileTooltipText : UIElementTooltipCustomText
{
    public string Text = "";

    public override string GetText() => Text;
}
